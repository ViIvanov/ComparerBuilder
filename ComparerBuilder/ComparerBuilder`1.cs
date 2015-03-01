using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GBricks.Collections
{
  using static Expression;
  using static ReplaceVisitor;

  [DebuggerDisplay("{DebuggerDisplay}")]
  public sealed class ComparerBuilder<T>
  {
    #region Cached Expression and Reflection objects

    private static readonly ConstantExpression Null = Constant(null);
    private static readonly ConstantExpression Zero = Constant(0);
    private static readonly ConstantExpression False = Constant(false);

    private static readonly ConstantExpression One = Constant(1);
    private static readonly ConstantExpression MinusOne = Constant(-1);

    private static readonly ParameterExpression X = Parameter(typeof(T), "x");
    private static readonly ParameterExpression Y = Parameter(typeof(T), "y");
    private static readonly ParameterExpression Obj = Parameter(typeof(T), "obj");

    private static readonly ParameterExpression Compare = Parameter(typeof(int));
    private static readonly IEnumerable<ParameterExpression> CompareVariables = new[] { Compare, };
    private static readonly LabelTarget Return = Label(typeof(int));
    private static readonly LabelExpression LabelZero = Label(Return, Zero);
    private static readonly GotoExpression ReturnZero = Return(Return, Zero);
    private static readonly GotoExpression ReturnOne = Return(Return, One);
    private static readonly GotoExpression ReturnMinusOne = Return(Return, MinusOne);
    private static readonly GotoExpression ReturnCompare = Return(Return, Compare);

    private static readonly Func<object, object, bool> ObjectEqualsDelegate = Equals;
    private static readonly Func<int> GetHashCodeDelegate = new object().GetHashCode;
    private static readonly Func<int, int, int> RotateRightDelegate = Comparers.RotateRight;

    private static readonly ConstructorInfo InvalidOperationExceptionCtor = typeof(InvalidOperationException).GetConstructor(new[] { typeof(string), });
    private static readonly PropertyInfo ExceptionDataProperty = typeof(Exception).GetProperty(nameof(Exception.Data));
    private static readonly MethodInfo ExceptionDataAddMethod = ExceptionDataProperty.PropertyType.GetMethod(nameof(IDictionary.Add), new Type[] { typeof(object), typeof(object), });

    private static readonly MethodInfo StringFormatMethod = new Func<string, object[], string>(String.Format).Method;

    private static readonly bool IsValueType = typeof(T).IsValueType;

    #endregion Cached Expression and Reflection objects

    public ComparerBuilder() { }

    private ComparerBuilder(ImmutableArray<IComparerExpression> expressions, ComparerBuilderInterception interception) {
      Expressions = expressions;
      Interception = interception;
    }

    private ImmutableArray<IComparerExpression> Expressions { get; }
    private ComparerBuilderInterception Interception { get; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"Expressions: {(Expressions.IsDefaultOrEmpty ? 0 : Expressions.Length)} item(s).";

    public bool IsEmpty => Expressions.IsDefaultOrEmpty;

    #region Add Expressions

    private ComparerBuilder<T> Add(IComparerExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var expressions = Expressions.IsDefaultOrEmpty ? ImmutableArray.Create(expression) : Expressions.Add(expression);
      return new ComparerBuilder<T>(expressions, Interception);
    }

    private ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, Expression equalityComparer, Expression comparisonComparer, SourceInfo sourceInfo) {
      var expr = new ComparerExpression<TProperty>(expression, equalityComparer, comparisonComparer, sourceInfo);
      return Add(expr);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) {
      var sourceInfo = new SourceInfo(filePath, lineNumber);
      return Add(expression, default(Expression), default(Expression), sourceInfo);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, IEqualityComparer<TProperty> equalityComparer, IComparer<TProperty> comparisonComparer, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) {
      var equality = Constant(equalityComparer ?? EqualityComparer<TProperty>.Default);
      var comparison = Constant(comparisonComparer ?? Comparer<TProperty>.Default);
      var sourceInfo = new SourceInfo(filePath, lineNumber);
      return Add(expression, equality, comparison, sourceInfo);
    }

    public ComparerBuilder<T> Add<TProperty, TComparer>(Expression<Func<T, TProperty>> expression, TComparer comparer, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) where TComparer : IEqualityComparer<TProperty>, IComparer<TProperty> {
      if(comparer == null) {
        throw new ArgumentNullException(nameof(comparer));
      }//if

      var constant = Constant(comparer);
      var sourceInfo = new SourceInfo(filePath, lineNumber);
      return Add(expression, constant, constant, sourceInfo);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, ComparerBuilder<TProperty> builder) {
      var expr = new NestedComparerExpression<TProperty>(expression, builder);
      return Add(expr);
    }

    public ComparerBuilder<T> Add(ComparerBuilder<T> other) {
      if(other == null) {
        throw new ArgumentNullException(nameof(other));
      }//if

      if(Expressions.IsDefaultOrEmpty || other.Expressions.IsDefaultOrEmpty) {
        return Expressions.IsDefaultOrEmpty ? other : this;
      } else {
        var expressions = Expressions.AddRange(other.Expressions);
        return new ComparerBuilder<T>(expressions, Interception);
      }//if
    }

    public ComparerBuilder<TDerived> ConvertTo<TDerived>() where TDerived : T {
      return new ComparerBuilder<TDerived>(Expressions, Interception);
    }

    public ComparerBuilder<T> WithInterception(ComparerBuilderInterception value) {
      if(value != Interception) {
        return new ComparerBuilder<T>(Expressions, value);
      } else {
        return this;
      }//if
    }

    #endregion Add Expressions

    #region Expression Helpers

    private static BinaryExpression IsNull(Expression value) => ReferenceEqual(value, Null);
    private static BinaryExpression IsNotNull(Expression value) => ReferenceNotEqual(value, Null);

    private static Expression ToObject(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var type = expression.Type;
      if(type.IsClass || type.IsInterface) {
        return expression;
      } else {
        return Convert(expression, typeof(object));
      }//if
    }

    #endregion Expression Helpers

    #region Build Methods

    private Expression<Func<T, T, bool>> BuildEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var expression = Expressions.Select(item => item.AsEquals(x, y, interception)).Aggregate((left, right) => AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : OrElse(ReferenceEqual(x, y), AndAlso(AndAlso(IsNotNull(x), IsNotNull(y)), expression));
      return Lambda<Func<T, T, bool>>(body, x, y);
    }

    private Expression<Func<T, int>> BuildGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null) {
      var list = Expressions.Select(item => item.AsGetHashCode(obj, interception)).ToList();
      var expression = list.Skip(1).Select((item, index) => Tuple.Create(item, index + 1))
        .Aggregate(list.First(), (acc, item) => ExclusiveOr(acc, Call(RotateRightDelegate.Method, item.Item1, Constant(item.Item2))));
      var body = IsValueType
        ? expression
        // ((object)obj == null) ? 0 : expression;
        : Condition(IsNull(obj), Zero, expression);
      return Lambda<Func<T, int>>(body, obj);
    }

    private Expression<Func<T, T, int>> BuildCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var reverse = Expressions.Select(item => item.AsCompare(x, y, interception)).Reverse().ToList();
      Expression seed = Return(Return, reverse.First());
      var expression = reverse.Skip(1).Aggregate(seed, (acc, value) => IfThenElse(NotEqual(Assign(Compare, value), Zero), ReturnCompare, acc));
      var body = IsValueType
        ? expression
        // if((object)x == (object)y) {
        //   return 0;
        // } else if((object)x == null) {
        //   return -1;
        // } else if((object)y == null) {
        //   return 1;
        // } else {
        //   return expression;
        // }//if
        : IfThenElse(ReferenceEqual(x, y), ReturnZero, IfThenElse(IsNull(x), ReturnMinusOne, IfThenElse(IsNull(y), ReturnOne, expression)));
      var block = Block(CompareVariables, body, LabelZero);
      return Lambda<Func<T, T, int>>(block, x, y);
    }

    #endregion Build Methods

    #region Build Comparers

    private void ThrowIfEmpty() {
      if(IsEmpty) {
        const string Message = "There are no expressions specified.";
        throw new InvalidOperationException(Message);
      }//if
    }

    private EqualityComparer<T> CreateEqualityComparer(ComparerBuilderInterception interception = null) {
      ThrowIfEmpty();
      var equals = BuildEquals(X, Y, interception);
      var hashCode = BuildGetHashCode(Obj, interception);
      return Comparers.Create(equals.Compile(), hashCode.Compile());
    }

    public EqualityComparer<T> CreateEqualityComparer() {
      return CreateEqualityComparer(interception: null);
    }

    public EqualityComparer<T> CreateEqualityComparerChecked(ComparerBuilderInterception interception = null) {
      return CreateEqualityComparer(interception ?? Interception ?? DefaultInterception.Instance);
    }

    private Comparer<T> CreateComparer(ComparerBuilderInterception interception = null) {
      ThrowIfEmpty();
      var compare = BuildCompare(X, Y, interception);
      return Comparers.Create(compare.Compile());
    }

    public Comparer<T> CreateComparer() {
      return CreateComparer(interception: null);
    }

    public Comparer<T> CreateComparerChecked(ComparerBuilderInterception interception = null) {
      return CreateComparer(interception ?? Interception ?? DefaultInterception.Instance);
    }

    #endregion Build Comparers

    #region Comparer Expressions

    private sealed class ComparerExpression<TProperty> : IComparerExpression
    {
      private static readonly Type[] InterceptTypeArguments = { typeof(TProperty), };

      public ComparerExpression(Expression<Func<T, TProperty>> expression, Expression equality = null, Expression comparison = null, SourceInfo sourceInfo = default(SourceInfo)) {
        if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        }//if

        Expression = expression;
        EqualityComparer = equality;
        Comparer = comparison;
        SourceInfo = sourceInfo;
      }

      public Expression<Func<T, TProperty>> Expression { get; }
      public Expression EqualityComparer { get; }
      public Expression Comparer { get; }
      public SourceInfo SourceInfo { get; }

      #region Comparison Helpers

      private static Expression MakeEquals(Expression x, Expression y, Expression comparer) {
        if(x == null) {
          throw new ArgumentNullException(nameof(x));
        } else if(y == null) {
          throw new ArgumentNullException(nameof(y));
        }//if

        if(comparer != null) {
          // comparer.Equals(x, y);
          return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.Equals), x, y);
        } else {
          if(x.Type.IsValueType && y.Type.IsValueType) {
            // x == y;
            return Equal(x, y);
          } else {
            // Object.Equals(x, y);
            return Call(ObjectEqualsDelegate.Method, ToObject(x), ToObject(y));
          }//if
        }//if
      }

      private static Expression MakeGetHashCode(Expression obj, Expression comparer) {
        if(obj == null) {
          throw new ArgumentNullException(nameof(obj));
        }//if

        if(comparer != null) {
          // comparer.GetHashCode(obj);
          return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.GetHashCode), obj);
        } else {
          var call = Call(obj, GetHashCodeDelegate.Method);
          if(obj.Type.IsValueType) {
            // obj.GetHashCode();
            return call;
          } else {
            // obj != null ? obj.GetHashCode() : 0
            return Condition(IsNotNull(obj), call, Zero);
          }//if
        }//if
      }

      private static Expression MakeCompare(Expression x, Expression y, Expression comparer) {
        if(x == null) {
          throw new ArgumentNullException(nameof(x));
        } else if(y == null) {
          throw new ArgumentNullException(nameof(y));
        }//if

        if(comparer != null) {
          // comparer.Compare(x, y);
          return CallComparerMethod(comparer, nameof(IComparer<T>.Compare), x, y);
        } else {
          // (x < y) ? -1 : (y < x ? 1 : 0);
          return Condition(LessThan(x, y), MinusOne, Condition(LessThan(y, x), One, Zero));
        }//if
      }

      private static Expression CallComparerMethod(Expression comparer, string methodName, params Expression[] arguments) {
        if(comparer == null) {
          throw new ArgumentNullException(nameof(comparer));
        }//if

        const BindingFlags MethodLookup = BindingFlags.Public | BindingFlags.Instance;
        var types = arguments != null && arguments.Any() ? Array.ConvertAll(arguments, item => item.Type) : Type.EmptyTypes;
        var method = comparer.Type.GetMethod(methodName, MethodLookup, null, types, null);
        if(method == null) {
          var message = $"Method \"{methodName}\" is not found in type \"{comparer.Type}\".";
          throw new ArgumentException(message, nameof(methodName));
        }//if

        return Call(comparer, method, arguments);
      }

      #endregion Comparison Helpers

      #region Interception

      private static Expression InterceptEquals(ComparerBuilderInterception interception, Expression expression, Expression x, Expression y, Expression comparer, SourceInfo sourceInfo) {
        if(interception == null) {
          return expression;
        } else {
          const string MethodName = nameof(ComparerBuilderInterception.InterceptEquals);
          var comparerType = typeof(IEqualityComparer<TProperty>);
          return ApplyInterception(interception, MethodName, expression, x, y, comparer, comparerType, sourceInfo, (a, b, c) => MakeEquals(a, b, c));
        }//if
      }

      private static Expression InterceptGetHashCode(ComparerBuilderInterception interception, Expression expression, Expression obj, Expression comparer, SourceInfo sourceInfo) {
        if(interception == null) {
          return expression;
        } else {
          const string MethodName = nameof(ComparerBuilderInterception.InterceptGetHashCode);
          var comparerType = typeof(IEqualityComparer<TProperty>);
          return ApplyInterception(interception, MethodName, expression, obj, null, comparer, comparerType, sourceInfo, (a, b, c) => MakeGetHashCode(a, c));
        }//if
      }

      private static Expression InterceptCompare(ComparerBuilderInterception interception, Expression expression, Expression x, Expression y, Expression comparer, SourceInfo sourceInfo) {
        if(interception == null) {
          return expression;
        } else {
          const string MethodName = nameof(ComparerBuilderInterception.InterceptCompare);
          var comparerType = typeof(IComparer<TProperty>);
          return ApplyInterception(interception, MethodName, expression, x, y, comparer, comparerType, sourceInfo, (a, b, c) => MakeCompare(a, b, c));
        }//if
      }

      private static Expression ApplyInterception(ComparerBuilderInterception interception, string methodName, Expression expression,
        Expression first, Expression second, Expression comparer, Type comparerType, SourceInfo sourceInfo,
        Func<Expression, Expression, Expression, Expression> make) {
        if(interception == null) {
          throw new ArgumentNullException(nameof(interception));
        } else if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        } else if(first == null) {
          throw new ArgumentNullException(nameof(first));
        } else if(make == null) {
          throw new ArgumentNullException(nameof(make));
        }//if

        // return {interception}.{methodName}<{valueType}>(expression, {expression}, {x}[, {y}], {comparer});

        var instance = Constant(interception);
        var expressionArg = Constant(expression, typeof(Expression));

        var firstArg = Parameter(first.Type);
        var assignFirst = Assign(firstArg, first);
        var useSecond = second != null;
        var secondArg = useSecond ? Parameter(second.Type) : null;
        var assignSecond = useSecond ? Assign(secondArg, second) : null;
        var variables = useSecond ? new[] { firstArg, secondArg, } : new[] { firstArg, };

        var valueArg = make(firstArg, secondArg, comparer);
        var comparerArg = comparer ?? Constant(null, comparerType);
        var sourceInfoArg = Constant(sourceInfo);
        var arguments = secondArg != null
          ? new[] { expressionArg, valueArg, firstArg, secondArg, comparerArg, sourceInfoArg, }
          : new[] { expressionArg, valueArg, firstArg, comparerArg, sourceInfoArg, };
        var call = Call(instance, methodName, InterceptTypeArguments, arguments);
        var expressions = useSecond
          ? new Expression[] { assignFirst, assignSecond, call, }
          : new Expression[] { assignFirst, call, };
        return Block(expression.Type, variables, expressions);
      }

      #endregion Interception

      public override string ToString() => Expression.ToString();

      #region IComparerExpression Members

      public Expression AsEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        var expression = MakeEquals(first, second, EqualityComparer);
        return InterceptEquals(interception, expression, first, second, EqualityComparer, SourceInfo);
      }

      public Expression AsGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null) {
        var value = ReplaceParameters(Expression, obj);
        var expression = MakeGetHashCode(value, EqualityComparer);
        return InterceptGetHashCode(interception, expression, value, EqualityComparer, SourceInfo);
      }

      public Expression AsCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        var expression = MakeCompare(first, second, Comparer);
        return InterceptCompare(interception, expression, first, second, Comparer, SourceInfo);
      }

      #endregion IComparerExpression Members
    }

    private sealed class NestedComparerExpression<TProperty> : IComparerExpression
    {
      public NestedComparerExpression(Expression<Func<T, TProperty>> expression, ComparerBuilder<TProperty> builder) {
        if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        } else if(builder == null) {
          throw new ArgumentNullException(nameof(builder));
        }//if

        Expression = expression;
        Builder = builder;
      }

      public Expression<Func<T, TProperty>> Expression { get; }
      public ComparerBuilder<TProperty> Builder { get; }

      private ComparerBuilderInterception GetInterception(ComparerBuilderInterception value)
        => value == DefaultInterception.Instance && Builder.Interception != null ? Builder.Interception : value;

      public override string ToString() => Expression.ToString();

      #region IComparerExpression Members

      public Expression AsEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
        var lambda = Builder.BuildEquals(ComparerBuilder<TProperty>.X, ComparerBuilder<TProperty>.Y, GetInterception(interception));
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        return ReplaceParameters(lambda, first, second);
      }

      public Expression AsGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null) {
        var lambda = Builder.BuildGetHashCode(ComparerBuilder<TProperty>.Obj, GetInterception(interception));
        var value = ReplaceParameters(Expression, obj);
        return ReplaceParameters(lambda, value);
      }

      public Expression AsCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
        var lambda = Builder.BuildCompare(ComparerBuilder<TProperty>.X, ComparerBuilder<TProperty>.Y, GetInterception(interception));
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        return ReplaceParameters(lambda, first, second);
      }

      #endregion IComparerExpression Members
    }

    #endregion Comparer Expressions
  }
}
