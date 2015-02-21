using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace GBricks.Collections
{
  [DebuggerDisplay("Expressions: {Expressions.Length}")]
  public sealed class ComparerBuilder<T>
  {
    #region Cached Expression and Reflection objects

    private static readonly ConstantExpression Null = Expression.Constant(null);
    private static readonly ConstantExpression Zero = Expression.Constant(0);
    private static readonly ConstantExpression False = Expression.Constant(false);

    private static readonly ConstantExpression One = Expression.Constant(1);
    private static readonly ConstantExpression MinusOne = Expression.Constant(-1);

    private static readonly ParameterExpression X = Expression.Parameter(typeof(T), "x");
    private static readonly ParameterExpression Y = Expression.Parameter(typeof(T), "y");
    private static readonly ParameterExpression Obj = Expression.Parameter(typeof(T), "obj");

    private static readonly ParameterExpression Compare = Expression.Parameter(typeof(int));
    private static readonly IEnumerable<ParameterExpression> CompareVariables = Enumerable.Repeat(Compare, 1);
    private static readonly LabelTarget Return = Expression.Label(typeof(int));
    private static readonly LabelExpression LabelZero = Expression.Label(Return, Zero);
    private static readonly GotoExpression ReturnZero = Expression.Return(Return, Zero);
    private static readonly GotoExpression ReturnOne = Expression.Return(Return, One);
    private static readonly GotoExpression ReturnMinusOne = Expression.Return(Return, MinusOne);
    private static readonly GotoExpression ReturnCompare = Expression.Return(Return, Compare);

    private static readonly Func<object, object, bool> ObjectEqualsDelegate = Equals;
    private static readonly Func<int> GetHashCodeDelegate = new object().GetHashCode;
    private static readonly Func<int, int, int> RotateRightDelegate = Comparers.RotateRight;

    private static readonly ConstructorInfo InvalidOperationExceptionCtor = typeof(InvalidOperationException).GetConstructor(new[] { typeof(string), });
    private static readonly PropertyInfo ExceptionDataProperty = typeof(Exception).GetProperty(nameof(Exception.Data));
    private static readonly MethodInfo ExceptionDataAddMethod = ExceptionDataProperty.PropertyType.GetMethod(nameof(IDictionary.Add), new Type[] { typeof(object), typeof(object), });

    private static readonly MethodInfo StringFormatMethod = new Func<string, object[], string>(String.Format).Method;

    private static readonly bool IsValueType = typeof(T).IsValueType;

    #endregion Cached Expression and Reflection objects

    public ComparerBuilder() : this(ImmutableArray<IComparerExpression>.Empty) { }

    private ComparerBuilder(ImmutableArray<IComparerExpression> expressions) {
      if(expressions.IsDefault) {
        throw new InvalidOperationException();
      }//if

      Expressions = expressions;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private ImmutableArray<IComparerExpression> Expressions { get; }

    #region Add Expressions

    private ComparerBuilder<T> Add(IComparerExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var expressions = Expressions.Add(expression);
      return new ComparerBuilder<T>(expressions);
    }

    public ComparerBuilder<T> Add(LambdaExpression expression, Expression equalityComparer = null, Expression comparisonComparer = null) {
      var expr = new ComparerExpression(expression, equalityComparer, comparisonComparer);
      return Add(expr);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, IEqualityComparer<TProperty> equalityComparer = null, IComparer<TProperty> comparisonComparer = null) {
      var equality = NullableConstant(equalityComparer);
      var comparison = NullableConstant(comparisonComparer);
      return Add(expression, equality, comparison);
    }

    public ComparerBuilder<T> Add<TProperty, TComparer>(Expression<Func<T, TProperty>> expression, TComparer comparer) where TComparer : IEqualityComparer<TProperty>, IComparer<TProperty> {
      var constant = NullableConstant(comparer);
      return Add(expression, constant, constant);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, ComparerBuilder<TProperty> builder) {
      var expr = new ChildComparerExpression<TProperty>(expression, builder);
      return Add(expr);
    }

    public ComparerBuilder<T> AddDefault(LambdaExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      Func<Type, string, Expression> create = (defitition, propertyName) => {
        var type = defitition.MakeGenericType(expression.ReturnType);
        var property = type.GetProperty(propertyName);
        var instance = property.GetValue(null, null);
        return Expression.Constant(instance);
      };

      var equality = create(typeof(EqualityComparer<>), nameof(EqualityComparer<T>.Default));
      var comparison = create(typeof(Comparer<>), nameof(Comparer<T>.Default));
      return Add(expression, equality, comparison);
    }

    public ComparerBuilder<T> AddDefault<TProperty>(Expression<Func<T, TProperty>> expression) {
      var equality = EqualityComparer<TProperty>.Default;
      var comparison = Comparer<TProperty>.Default;
      return Add(expression, equality, comparison);
    }

    public ComparerBuilder<T> Add(ComparerBuilder<T> other) {
      if(other == null) {
        throw new ArgumentNullException(nameof(other));
      }//if

      var expressions = Expressions.AddRange(other.Expressions);
      return new ComparerBuilder<T>(expressions);
    }

    public ComparerBuilder<TDerived> AsDerived<TDerived>() where TDerived : T {
      return new ComparerBuilder<TDerived>(Expressions);
    }

    #endregion Add Expressions

    #region Expression Helpers

    private static BinaryExpression ReferenceEqual(Expression left, Expression right) => Expression.ReferenceEqual(left, right);
    private static BinaryExpression IsNull(Expression value) => Expression.ReferenceEqual(value, Null);
    private static BinaryExpression IsNotNull(Expression value) => Expression.ReferenceNotEqual(value, Null);
    private static ConstantExpression NullableConstant(object value) => value != null ? Expression.Constant(value) : null;

    private static Expression ToObject(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var type = expression.Type;
      if(type.IsClass || type.IsInterface) {
        return expression;
      } else {
        return Expression.Convert(expression, typeof(object));
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
          return Expression.Equal(x, y);
        } else {
          // Object.Equals(x, y);
          return Expression.Call(ObjectEqualsDelegate.Method, ToObject(x), ToObject(y));
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
        var call = Expression.Call(obj, GetHashCodeDelegate.Method);
        if(obj.Type.IsValueType) {
          // obj.GetHashCode();
          return call;
        } else {
          // obj != null ? obj.GetHashCode() : 0
          return Expression.Condition(IsNotNull(obj), call, Zero);
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
        return Expression.Condition(Expression.LessThan(x, y), MinusOne,
          Expression.Condition(Expression.LessThan(y, x), One, Zero));
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

      return Expression.Call(comparer, method, arguments);
    }

    private static Expression ApplyAssert(LambdaExpression assert, Expression expression, Expression x, Expression y) {
      if(assert == null) {
        throw new ArgumentNullException(nameof(assert));
      } else if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var format = Expression.Constant("Failed to compare values \"{0}\" and \"{1}\" in the expression \"{2}\".");
      var xAsObject = ToObject(x);
      var yAsObject = ToObject(y);
      var args = Expression.NewArrayInit(typeof(object), xAsObject, yAsObject, Expression.Constant(expression.ToString()));
      var message = Expression.Call(StringFormatMethod, format, args);
      var exceptionNew = Expression.New(InvalidOperationExceptionCtor, message);
      var addData = AddExceptionData(new Dictionary<Expression, Expression>(2) {
        [Expression.Constant(nameof(x))] = xAsObject,
        [Expression.Constant(nameof(y))] = yAsObject,
      });
      var exceptionInit = Expression.MemberInit(exceptionNew, addData);
      Expression @throw = Expression.Throw(exceptionInit);

      var result = Expression.Parameter(expression.Type);
      var assign = Expression.Assign(result, expression);
      var preparedAssert = ReplaceParameters(assert, assign);
      var test = Expression.Not(preparedAssert);
      var check = Expression.IfThen(test, @throw);
      return Expression.Block(result.Type, new[] { result, }, check, result);
    }

    private static MemberListBinding AddExceptionData(IDictionary<Expression, Expression> items) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var elements =
          from item in items
          select Expression.ElementInit(ExceptionDataAddMethod, item.Key, item.Value);
      return Expression.ListBind(ExceptionDataProperty, elements);
    }

    #endregion Comparison Helpers

    #region Replace Parameters

    private static Expression ReplaceParameters(LambdaExpression expression, params Expression[] parameters) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(parameters == null) {
        throw new ArgumentNullException(nameof(parameters));
      } else if(expression.Parameters.Count != parameters.Length) {
        throw new ArgumentException("expression.Parameters.Count != parameters.Length", nameof(parameters));
      }//if

      if(!expression.Parameters.Any()) {
        return expression;
      }//if

      var items = expression.Parameters.Zip(parameters, (key, value) => new KeyValuePair<Expression, Expression>(key, value));
      var replace = new ReplaceVisitor(items);
      return replace.Visit(expression.Body);
    }

    private sealed class ReplaceVisitor : ExpressionVisitor
    {
      public ReplaceVisitor(IEnumerable<KeyValuePair<Expression, Expression>> items) {
        if(items == null) {
          throw new ArgumentNullException(nameof(items));
        }//if

        var dictionary = new Dictionary<Expression, Expression>();
        foreach(var item in items) {
          dictionary.Add(item.Key, item.Value);
        }//for

        Items = new ReadOnlyDictionary<Expression, Expression>(dictionary);
      }

      public IReadOnlyDictionary<Expression, Expression> Items { get; }

      public override Expression Visit(Expression node) {
        Expression to;
        if(node != null && Items.TryGetValue(node, out to)) {
          return to;
        } else {
          return base.Visit(node);
        }//if
      }
    }

    #endregion Replace Parameters

    #region Build Methods

    private Expression<Func<T, T, bool>> BuildEquals(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
      var items =
        from item in Expressions
        select item.AsEquals(x, y, assert);
      return AggregateEquals(items, x, y);
    }

    private Expression<Func<T, int>> BuildGetHashCode(ParameterExpression obj) {
      var items =
        from item in Expressions
        select item.AsGetHashCode(obj);
      return AggregateGetHashCode(items, obj);
    }

    private Expression<Func<T, T, int>> BuildCompare(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null) {
      var items =
        from item in Expressions
        select item.AsCompare(x, y, assert);
      return AggregateCompare(items, x, y);
    }

    private static Expression<Func<T, T, bool>> AggregateEquals(IEnumerable<Expression> items, ParameterExpression x, ParameterExpression y) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var expression = items.Aggregate((left, right) => Expression.AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : Expression.OrElse(
            ReferenceEqual(x, y),
            Expression.AndAlso(
              Expression.AndAlso(IsNotNull(x), IsNotNull(y)),
              expression));
      return Expression.Lambda<Func<T, T, bool>>(body, x, y);
    }

    private static Expression<Func<T, int>> AggregateGetHashCode(IEnumerable<Expression> items, ParameterExpression obj) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var list = items as IReadOnlyCollection<Expression> ?? items.ToList();
      var expression = list.Skip(1).Select((item, index) => Tuple.Create(item, index + 1))
        .Aggregate(list.First(), (acc, item) =>
          Expression.ExclusiveOr(acc,
            Expression.Call(RotateRightDelegate.Method, item.Item1, Expression.Constant(item.Item2))));
      var body = IsValueType
        ? expression
        // ((object)obj == null) ? 0 : expression;
        : Expression.Condition(IsNull(obj), Zero, expression);
      return Expression.Lambda<Func<T, int>>(body, obj);
    }

    private static Expression<Func<T, T, int>> AggregateCompare(IEnumerable<Expression> items, ParameterExpression x, ParameterExpression y) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var reverse = items.Reverse().ToList();
      Expression seed = Expression.Return(Return, reverse.First());
      var expression = reverse.Skip(1).Aggregate(seed, (acc, value) => Expression.IfThenElse(Expression.NotEqual(Expression.Assign(Compare, value), Zero), ReturnCompare, acc));
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
        : Expression.IfThenElse(ReferenceEqual(x, y), ReturnZero,
            Expression.IfThenElse(IsNull(x), ReturnMinusOne,
              Expression.IfThenElse(IsNull(y), ReturnOne, expression)));
      var block = Expression.Block(CompareVariables, body, LabelZero);
      return Expression.Lambda<Func<T, T, int>>(block, x, y);
    }

    #endregion Build Methods

    #region Build Comparers

    private EqualityComparer<T> CreateEqualityComparer(Expression<Func<bool, bool>> assert) {
      if(!Expressions.Any()) {
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
      if(!Expressions.Any()) {
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

    private sealed class ComparerExpression : IComparerExpression
    {
      public ComparerExpression(LambdaExpression expression, Expression equality = null, Expression comparison = null) {
        if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        }//if

        Expression = expression;
        EqualityComparer = equality;
        Comparer = comparison;
      }

      public LambdaExpression Expression { get; }
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

    private sealed class ChildComparerExpression<TProperty> : IComparerExpression
    {
      public ChildComparerExpression(LambdaExpression expression, ComparerBuilder<TProperty> builder) {
        if(expression == null) {
          throw new ArgumentNullException(nameof(expression));
        } else if(builder == null) {
          throw new ArgumentNullException(nameof(builder));
        }//if

        Expression = expression;
        Builder = builder;
      }

      public LambdaExpression Expression { get; }
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
