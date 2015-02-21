using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ComparerBuilder
{
  [DebuggerDisplay("Expressions: {System.Linq.Enumerable.Count(GetExpressions())}")]
  public sealed class ComparerBuilder<T> : IComparerBuilder<T>
  {
    #region Cached Expression and Reflection objects

    private static readonly ConstantExpression Null = Expression.Constant(null);
    private static readonly ConstantExpression Zero = Expression.Constant(0);
    private static readonly ConstantExpression False = Expression.Constant(false);

    private static readonly ConstantExpression One = Expression.Constant(1);
    private static readonly ConstantExpression MinusOne = Expression.Constant(-1);

    private static readonly ParameterExpression First = Expression.Parameter(typeof(T), null);
    private static readonly ParameterExpression Second = Expression.Parameter(typeof(T), null);

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

    public ComparerBuilder() {
    }

    private List<IComparerBuilder> Builders { get; } = new List<IComparerBuilder>();

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private List<IComparerExpression> Expressions { get; } = new List<IComparerExpression>();

    #region Add Expressions

    private void AddExpression(IComparerExpression expression) {
      Expressions.Add(expression);
    }

    private void AddExpression(LambdaExpression expression, Expression equalityComparer = null, Expression comparisonComparer = null) {
      var expr = new SimpleComparerExpression(expression, equalityComparer, comparisonComparer);
      AddExpression(expr);
    }

    public ComparerBuilder<T> Add(LambdaExpression expression, Expression equalityComparer = null, Expression comparisonComparer = null) {
      AddExpression(expression, equalityComparer, comparisonComparer);
      return this;
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

    public ComparerBuilder<T> Add(IComparerBuilder<T> builder) {
      if(builder == null) {
        throw new ArgumentNullException(nameof(builder));
      }//if

      Builders.Add(builder);
      return this;
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, ComparerBuilder<TProperty> builder) {
      var expr = new ComplexComparerExpression<TProperty>(expression, builder);
      Expressions.Add(expr);
      return this;
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

    private static Expression MakeEquals(Expression left, Expression right, Expression comparer) {
      if(left == null) {
        throw new ArgumentNullException(nameof(left));
      } else if(right == null) {
        throw new ArgumentNullException(nameof(right));
      }//if

      if(comparer != null) {
        // comparer.Equals(left, right);
        return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.Equals), left, right);
      } else {
        if(left.Type.IsValueType && right.Type.IsValueType) {
          // left == right;
          return Expression.Equal(left, right);
        } else {
          // Object.Equals(left, right);
          return Expression.Call(ObjectEqualsDelegate.Method, ToObject(left), ToObject(right));
        }//if
      }//if
    }

    private static Expression MakeGetHashCode(Expression value, Expression comparer) {
      if(value == null) {
        throw new ArgumentNullException(nameof(value));
      }//if

      if(comparer != null) {
        // comparer.GetHashCode(value);
        return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.GetHashCode), value);
      } else {
        var call = Expression.Call(value, GetHashCodeDelegate.Method);
        if(value.Type.IsValueType) {
          // value.GetHashCode();
          return call;
        } else {
          // value != null ? value.GetHashCode() : 0
          return Expression.Condition(IsNotNull(value), call, Zero);
        }//if
      }//if
    }

    private static Expression MakeCompare(Expression left, Expression right, Expression comparer) {
      if(left == null) {
        throw new ArgumentNullException(nameof(left));
      } else if(right == null) {
        throw new ArgumentNullException(nameof(right));
      }//if

      if(comparer != null) {
        // comparer.Compare(left, right);
        return CallComparerMethod(comparer, nameof(IComparer<T>.Compare), left, right);
      } else {
        // (left < right) ? -1 : (left > right ? 1 : 0);
        return Expression.Condition(Expression.LessThan(left, right), MinusOne,
          Expression.Condition(Expression.GreaterThan(left, right), One, Zero));
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

    private static Expression ApplyAssert(LambdaExpression assert, Expression expression, Expression left, Expression right) {
      if(assert == null) {
        throw new ArgumentNullException(nameof(assert));
      } else if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var format = Expression.Constant("Failed to compare values \"{0}\" and \"{1}\" in the expression \"{2}\".");
      var leftAsObject = ToObject(left);
      var rightAsObject = ToObject(right);
      var args = Expression.NewArrayInit(typeof(object), leftAsObject, rightAsObject, Expression.Constant(expression.ToString()));
      var message = Expression.Call(StringFormatMethod, format, args);
      var exceptionNew = Expression.New(InvalidOperationExceptionCtor, message);
      var addData = AddExceptionData(new Dictionary<Expression, Expression>(2) {
        [Expression.Constant("Left")] = leftAsObject,
        [Expression.Constant("Right")] = rightAsObject,
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

    private IEnumerable<IComparerExpression> GetExpressions() {
      var builders =
        from builder in Builders
        from expression in builder.Expressions
        select expression;
      var expressions =
        from expression in Expressions
        select expression;
      return builders.Concat(expressions);
    }

    private Expression<Func<T, T, bool>> BuildEquals(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
      var items =
        from item in GetExpressions()
        select item.BuildEquals(first, second, assert);
      return AggregateEquals(items, first, second);
    }

    private Expression<Func<T, int>> BuildGetHashCode(ParameterExpression parameter) {
      var items =
        from item in GetExpressions()
        select item.BuildGetHashCode(parameter);
      return AggregateGetHashCode(items, parameter);
    }

    private Expression<Func<T, T, int>> BuildCompare(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
      var items =
        from item in GetExpressions()
        select item.BuildCompare(first, second, assert);
      return AggregateCompare(items, first, second);
    }

    private static Expression<Func<T, T, bool>> AggregateEquals(IEnumerable<Expression> items, ParameterExpression first, ParameterExpression second) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var expression = items.Aggregate((left, right) => Expression.AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : Expression.OrElse(
            ReferenceEqual(first, second),
            Expression.AndAlso(
              Expression.AndAlso(IsNotNull(first), IsNotNull(second)),
              expression));
      return Expression.Lambda<Func<T, T, bool>>(body, first, second);
    }

    private static Expression<Func<T, int>> AggregateGetHashCode(IEnumerable<Expression> items, ParameterExpression parameter) {
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
        // ((object)x == null) ? 0 : expression;
        : Expression.Condition(IsNull(parameter), Zero, expression);
      return Expression.Lambda<Func<T, int>>(body, parameter);
    }

    private static Expression<Func<T, T, int>> AggregateCompare(IEnumerable<Expression> items, ParameterExpression first, ParameterExpression second) {
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
        : Expression.IfThenElse(ReferenceEqual(first, second), ReturnZero,
            Expression.IfThenElse(IsNull(first), ReturnMinusOne,
              Expression.IfThenElse(IsNull(second), ReturnOne, expression)));
      var block = Expression.Block(CompareVariables, body, LabelZero);
      return Expression.Lambda<Func<T, T, int>>(block, first, second);
    }

    #endregion Build Methods

    #region Build Comparers

    private EqualityComparer<T> CreateEqualityComparer(Expression<Func<bool, bool>> assert) {
      if(!Expressions.Any()) {
        return Comparers.EmptyEqualityComparer<T>();
      }//if

      var equals = BuildEquals(First, Second, assert);
      var hashCode = BuildGetHashCode(First);
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

      var compare = BuildCompare(First, Second, assert);
      return Comparers.Create(compare.Compile());
    }

    public Comparer<T> CreateComparer() {
      return CreateComparer(assert: null);
    }

    public Comparer<T> CreateComparerChecked(Expression<Func<int, bool>> assert = null) {
      return CreateComparer(assert ?? (value => value == 0));
    }

    #endregion Build Comparers

    #region IComparerBuilder Members

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    IReadOnlyList<IComparerExpression> IComparerBuilder.Expressions => Expressions;

    #endregion IComparerBuilder Members

    private sealed class SimpleComparerExpression : IComparerExpression
    {
      public SimpleComparerExpression(LambdaExpression lambda, Expression equality = null, Expression comparison = null) {
        if(lambda == null) {
          throw new ArgumentNullException(nameof(lambda));
        }//if

        Lambda = lambda;
        EqualityComparer = equality;
        Comparer = comparison;
      }

      public LambdaExpression Lambda { get; }
      public Expression EqualityComparer { get; }
      public Expression Comparer { get; }

      public override string ToString() => Lambda.ToString();

      #region IComparerExpression Members

      public Expression BuildEquals(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
        var left = ReplaceParameters(Lambda, first);
        var right = ReplaceParameters(Lambda, second);
        var expression = MakeEquals(left, right, EqualityComparer);
        return assert != null ? ApplyAssert(assert, expression, left, right) : expression;
      }

      public Expression BuildGetHashCode(ParameterExpression parameter) {
        var param = ReplaceParameters(Lambda, parameter);
        return MakeGetHashCode(param, EqualityComparer);
      }

      public Expression BuildCompare(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
        var left = ReplaceParameters(Lambda, first);
        var right = ReplaceParameters(Lambda, second);
        var expression = MakeCompare(left, right, Comparer);
        return assert != null ? ApplyAssert(assert, expression, left, right) : expression;
      }

      #endregion IComparerExpression Members
    }

    private sealed class ComplexComparerExpression<TValue> : IComparerExpression
    {
      public ComplexComparerExpression(LambdaExpression lambda, ComparerBuilder<TValue> builder) {
        if(lambda == null) {
          throw new ArgumentNullException(nameof(lambda));
        } else if(builder == null) {
          throw new ArgumentNullException(nameof(builder));
        }//if

        Lambda = lambda;
        Builder = builder;
      }

      public LambdaExpression Lambda { get; }
      public ComparerBuilder<TValue> Builder { get; }

      public override string ToString() => Lambda.ToString();

      #region IComparerExpression Members

      public Expression BuildEquals(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
        var lambda = Builder.BuildEquals(ComparerBuilder<TValue>.First, ComparerBuilder<TValue>.Second, assert);
        var left = ReplaceParameters(Lambda, first);
        var right = ReplaceParameters(Lambda, second);
        return ReplaceParameters(lambda, left, right);
      }

      public Expression BuildGetHashCode(ParameterExpression parameter) {
        var lambda = Builder.BuildGetHashCode(ComparerBuilder<TValue>.First);
        var param = ReplaceParameters(Lambda, parameter);
        return ReplaceParameters(lambda, param);
      }

      public Expression BuildCompare(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null) {
        var lambda = Builder.BuildCompare(ComparerBuilder<TValue>.First, ComparerBuilder<TValue>.Second, assert);
        var left = ReplaceParameters(Lambda, first);
        var right = ReplaceParameters(Lambda, second);
        return ReplaceParameters(lambda, left, right);
      }

      #endregion IComparerExpression Members
    }
  }
}
