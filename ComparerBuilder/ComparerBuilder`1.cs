using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace GBricks.Collections
{
  using static Expression;

  [DebuggerDisplay("Expressions: {Expressions.Length}")]
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

    private ComparerBuilder(ImmutableArray<IComparerExpression> expressions) {
      Expressions = expressions;
    }

    private ImmutableArray<IComparerExpression> Expressions { get; }

    #region Add Expressions

    private ComparerBuilder<T> Add(IComparerExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var expressions = Expressions.IsDefaultOrEmpty ? ImmutableArray.Create(expression) : Expressions.Add(expression);
      return new ComparerBuilder<T>(expressions);
    }

    private ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, Expression equalityComparer, Expression comparisonComparer) {
      var expr = new ComparerExpression<TProperty>(expression, equalityComparer, comparisonComparer);
      return Add(expr);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, IEqualityComparer<TProperty> equalityComparer = null, IComparer<TProperty> comparisonComparer = null) {
      var equality = Constant(equalityComparer ?? EqualityComparer<TProperty>.Default);
      var comparison = Constant(comparisonComparer ?? Comparer<TProperty>.Default);
      return Add(expression, equality, comparison);
    }

    public ComparerBuilder<T> Add<TProperty, TComparer>(Expression<Func<T, TProperty>> expression, TComparer comparer) where TComparer : IEqualityComparer<TProperty>, IComparer<TProperty> {
      if(comparer == null) {
        throw new ArgumentNullException(nameof(comparer));
      }//if

      if(comparer == null) {
        return AddAuto(expression);
      } else {
        var constant = Constant(comparer);
        return Add(expression, constant, constant);
      }//if
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, ComparerBuilder<TProperty> builder) {
      var expr = new NestedComparerExpression<TProperty>(expression, builder);
      return Add(expr);
    }

    public ComparerBuilder<T> AddAuto<TProperty>(Expression<Func<T, TProperty>> expression) {
      return Add(expression, default(Expression), default(Expression));
    }

    public ComparerBuilder<T> Add(ComparerBuilder<T> other) {
      if(other == null) {
        throw new ArgumentNullException(nameof(other));
      }//if

      if(Expressions.IsDefaultOrEmpty || other.Expressions.IsDefaultOrEmpty) {
        return Expressions.IsDefaultOrEmpty ? other : this;
      } else {
        var expressions = Expressions.AddRange(other.Expressions);
        return new ComparerBuilder<T>(expressions);
      }//if
    }

    public ComparerBuilder<TDerived> ConvertTo<TDerived>() where TDerived : T {
      return new ComparerBuilder<TDerived>(Expressions);
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

    private static Expression ApplyAssert(LambdaExpression assert, Expression expression, Expression x, Expression y) {
      if(assert == null) {
        throw new ArgumentNullException(nameof(assert));
      } else if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var format = Constant("Failed to compare values \"{0}\" and \"{1}\" in the expression \"{2}\".");
      var xAsObject = ToObject(x);
      var yAsObject = ToObject(y);
      var args = NewArrayInit(typeof(object), xAsObject, yAsObject, Constant(expression.ToString()));
      var message = Call(StringFormatMethod, format, args);
      var exceptionNew = New(InvalidOperationExceptionCtor, message);
      var initX = ElementInit(ExceptionDataAddMethod, Constant(nameof(x)), xAsObject);
      var initY = ElementInit(ExceptionDataAddMethod, Constant(nameof(y)), yAsObject);
      var addData = ListBind(ExceptionDataProperty, initX, initY);
      var exceptionInit = MemberInit(exceptionNew, addData);
      var @throw = Throw(exceptionInit);

      var result = Parameter(expression.Type);
      var assign = Assign(result, expression);
      var preparedAssert = ReplaceParameters(assert, assign);
      var test = Not(preparedAssert);
      var check = IfThen(test, @throw);
      return Block(result.Type, new[] { result, }, check, result);
    }

    #endregion Comparison Helpers

    #region Replace Parameters

    private static Expression ReplaceParameters(LambdaExpression expression, Expression first, Expression second = null) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(expression.Parameters.Count != (second == null ? 1 : 2)) {
        throw new ArgumentException($"expression.Parameters.Count != {(second == null ? 1 : 2)}", nameof(expression));
      }//if

      var replace = second == null
        ? new ReplaceVisitor(expression.Parameters[0], first)
        : new ReplaceVisitor(expression.Parameters[0], first, expression.Parameters[1], second);
      return replace.Visit(expression.Body);
    }

    private sealed class ReplaceVisitor : ExpressionVisitor
    {
      public ReplaceVisitor(Expression what, Expression to) {
        if(what == null) {
          throw new ArgumentNullException(nameof(what));
        } else if(to == null) {
          throw new ArgumentNullException(nameof(to));
        }//if

        What = what;
        To = to;
      }

      public ReplaceVisitor(Expression what, Expression to, Expression secondWhat, Expression secondTo) : this(what, to) {
        if(secondWhat == null) {
          throw new ArgumentNullException(nameof(secondWhat));
        } else if(secondTo == null) {
          throw new ArgumentNullException(nameof(secondTo));
        }//if

        SecondWhat = secondWhat;
        SecondTo = secondTo;
      }

      public Expression What { get; }
      public Expression To { get; }

      public Expression SecondWhat { get; }
      public Expression SecondTo { get; }

      public override Expression Visit(Expression node) {
        if(node == What) {
          return To;
        } else if(node != null && node == SecondWhat) {
          return SecondTo;
        } else {
          return base.Visit(node);
        }//if
      }
    }

    #endregion Replace Parameters

    #region Build Methods

    private Expression<Func<T, T, bool>> BuildEquals(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
      var expression = Expressions.Select(item => item.AsEquals(x, y, assert)).Aggregate((left, right) => AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : OrElse(ReferenceEqual(x, y), AndAlso(AndAlso(IsNotNull(x), IsNotNull(y)), expression));
      return Lambda<Func<T, T, bool>>(body, x, y);
    }

    private Expression<Func<T, int>> BuildGetHashCode(ParameterExpression obj) {
      var list = Expressions.Select(item => item.AsGetHashCode(obj)).ToList();
      var expression = list.Skip(1).Select((item, index) => Tuple.Create(item, index + 1))
        .Aggregate(list.First(), (acc, item) => ExclusiveOr(acc, Call(RotateRightDelegate.Method, item.Item1, Constant(item.Item2))));
      var body = IsValueType
        ? expression
        // ((object)obj == null) ? 0 : expression;
        : Condition(IsNull(obj), Zero, expression);
      return Lambda<Func<T, int>>(body, obj);
    }

    private Expression<Func<T, T, int>> BuildCompare(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
      var reverse = Expressions.Select(item => item.AsCompare(x, y, assert)).Reverse().ToList();
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

    private EqualityComparer<T> CreateEqualityComparer(Expression<Func<bool, bool>> assert) {
      if(Expressions.IsDefaultOrEmpty) {
        return Comparers.EmptyEqualityComparer<T>();
      }//if

      var equals = BuildEquals(X, Y, assert);
      var hashCode = BuildGetHashCode(Obj);
      return Comparers.Create(equals.Compile(), hashCode.Compile());
    }

    public EqualityComparer<T> CreateEqualityComparer() {
      return CreateEqualityComparer(assert: null);
    }

    public EqualityComparer<T> CreateEqualityComparerChecked(Expression<Func<bool, bool>> assert = null) {
      return CreateEqualityComparer(assert ?? (value => value));
    }

    private Comparer<T> CreateComparer(Expression<Func<int, bool>> assert) {
      if(Expressions.IsDefaultOrEmpty) {
        return Comparers.EmptyComparer<T>();
      }//if

      var compare = BuildCompare(X, Y, assert);
      return Comparers.Create(compare.Compile());
    }

    public Comparer<T> CreateComparer() {
      return CreateComparer(assert: null);
    }

    public Comparer<T> CreateComparerChecked(Expression<Func<int, bool>> assert = null) {
      return CreateComparer(assert ?? (value => value == 0));
    }

    #endregion Build Comparers

    #region Comparer Expressions

    private sealed class ComparerExpression<TProperty> : IComparerExpression
    {
      public ComparerExpression(Expression<Func<T, TProperty>> expression, Expression equality = null, Expression comparison = null) {
        if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        }//if

        Expression = expression;
        EqualityComparer = equality;
        Comparer = comparison;
      }

      public Expression<Func<T, TProperty>> Expression { get; }
      public Expression EqualityComparer { get; }
      public Expression Comparer { get; }

      public override string ToString() => Expression.ToString();

      #region IComparerExpression Members

      public Expression AsEquals(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        var expression = MakeEquals(first, second, EqualityComparer);
        return assert != null ? ApplyAssert(assert, expression, first, second) : expression;
      }

      public Expression AsGetHashCode(ParameterExpression obj) {
        var value = ReplaceParameters(Expression, obj);
        return MakeGetHashCode(value, EqualityComparer);
      }

      public Expression AsCompare(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        var expression = MakeCompare(first, second, Comparer);
        return assert != null ? ApplyAssert(assert, expression, first, second) : expression;
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

      public override string ToString() => Expression.ToString();

      #region IComparerExpression Members

      public Expression AsEquals(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
        var lambda = Builder.BuildEquals(ComparerBuilder<TProperty>.X, ComparerBuilder<TProperty>.Y, assert);
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        return ReplaceParameters(lambda, first, second);
      }

      public Expression AsGetHashCode(ParameterExpression obj) {
        var lambda = Builder.BuildGetHashCode(ComparerBuilder<TProperty>.Obj);
        var value = ReplaceParameters(Expression, obj);
        return ReplaceParameters(lambda, value);
      }

      public Expression AsCompare(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
        var lambda = Builder.BuildCompare(ComparerBuilder<TProperty>.X, ComparerBuilder<TProperty>.Y, assert);
        var first = ReplaceParameters(Expression, x);
        var second = ReplaceParameters(Expression, y);
        return ReplaceParameters(lambda, first, second);
      }

      #endregion IComparerExpression Members
    }

    #endregion Comparer Expressions
  }
}
