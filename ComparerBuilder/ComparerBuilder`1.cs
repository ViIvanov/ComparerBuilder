using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ComparerBuilder
{
  public class ComparerBuilder<T> : IEnumerable<Expression>
  {
    private static readonly ConstantExpression Null = Expression.Constant(null);
    private static readonly ConstantExpression Zero = Expression.Constant(0);
    private static readonly ConstantExpression False = Expression.Constant(false);

    private static readonly ConstantExpression One = Expression.Constant(1);
    private static readonly ConstantExpression MinusOne = Expression.Constant(-1);

    private static readonly ParameterExpression Left = Expression.Parameter(typeof(T), null);
    private static readonly ParameterExpression Right = Expression.Parameter(typeof(T), null);

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

    private const string EqualsMethodName = nameof(IEqualityComparer<T>.Equals);
    private const string GetHashCodeMethodName = nameof(IEqualityComparer<T>.GetHashCode);
    private const string CompareMethodName = nameof(IComparer<T>.Compare);

    private static readonly ConstructorInfo InvalidOperationExceptionCtor = typeof(InvalidOperationException).GetConstructor(new[] { typeof(string), });
    private static readonly PropertyInfo ExceptionDataProperty = typeof(Exception).GetProperty(nameof(Exception.Data));
    private static readonly MethodInfo ExceptionDataAddMethod = ExceptionDataProperty.PropertyType.GetMethod(nameof(IDictionary.Add), new Type[] { typeof(object), typeof(object), });

    private static readonly MethodInfo StringFormatMethod = new Func<string, object[], string>(String.Format).Method;

    private static readonly bool IsValueType = typeof(T).IsValueType;

    public ComparerBuilder() {
      Expressions = new List<ComparerExpression>();
    }

    private IList<ComparerExpression> Expressions { get; }

    #region Expression Helpers

    private static BinaryExpression ReferenceEqual(Expression left, Expression right) => Expression.ReferenceEqual(left, right);
    private static BinaryExpression IsNull(Expression value) => Expression.ReferenceEqual(value, Null);
    private static BinaryExpression IsNotNull(Expression value) => Expression.ReferenceNotEqual(value, Null);
    private static ConstantExpression NullableConstant(object value) => value != null ? Expression.Constant(value) : null;

    private static Expression CallComparerMethod(Expression comparer, string methodName, params Expression[] arguments) {
      if(comparer == null) {
        throw new ArgumentNullException(nameof(comparer));
      }//if

      const BindingFlags MethodLookup = BindingFlags.Public | BindingFlags.Instance;
      var types = arguments != null && arguments.Any() ? Array.ConvertAll(arguments, item => item.Type) : Type.EmptyTypes;
      var method = comparer.Type.GetMethod(methodName, MethodLookup, null, types, null);
      if(method == null) {
        const string Format = "Method \"{0}\" is not found in type \"{1}\".";
        var message = String.Format(Format, methodName, comparer.Type);
        throw new ArgumentException(message, nameof(methodName));
      }//if

      return Expression.Call(comparer, method, arguments);
    }

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

    private static MemberListBinding AddExceptionData(IDictionary<Expression, Expression> items) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var elements =
          from item in items
          select Expression.ElementInit(ExceptionDataAddMethod, item.Key, item.Value);
      return Expression.ListBind(ExceptionDataProperty, elements);
    }

    #endregion Expression Helpers

    protected virtual Expression ApplyAssert(LambdaExpression assert, Expression expression, Expression left, Expression right) {
      if(assert == null) {
        throw new ArgumentNullException(nameof(assert));
      } else if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var format = Expression.Constant("Failed to compare values {0} and {1} in the expression \"{2}\".");
      var oleft = ToObject(left);
      var oright = ToObject(right);
      var args = Expression.NewArrayInit(typeof(object), oleft, oright, Expression.Constant(expression.ToString()));
      var message = Expression.Call(StringFormatMethod, format, args);
      var exceptionNew = Expression.New(InvalidOperationExceptionCtor, message);
      var addData = AddExceptionData(new Dictionary<Expression, Expression>(2) {
        [Expression.Constant("Left")] = oleft,
        [Expression.Constant("Right")] = oright,
      });
      var exceptionInit = Expression.MemberInit(exceptionNew, addData);
      Expression @throw = Expression.Throw(exceptionInit);

      var result = Expression.Parameter(expression.Type);
      var assign = Expression.Assign(result, expression);
      var preparedAssert = ReplaceParameter(assert, assign);
      var test = Expression.Not(preparedAssert);
      var check = Expression.IfThen(test, @throw);
      return Expression.Block(result.Type, new[] { result, }, check, result);
    }

    private static Expression MakeEquals(Expression left, Expression right, Expression comparer) {
      if(left == null) {
        throw new ArgumentNullException(nameof(left));
      } else if(right == null) {
        throw new ArgumentNullException(nameof(right));
      }//if

      if(comparer != null) {
        // comparer.Equals(left, right);
        return CallComparerMethod(comparer, EqualsMethodName, left, right);
      } else {
        if(left.Type.IsValueType && right.Type.IsValueType) {
          // left == right;
          return Expression.Equal(left, right);
        } else {
          // Object.Equals(left, right);
          return Expression.Call(ObjectEqualsDelegate.Method, left, right);
        }//if
      }//if
    }

    private static Expression MakeGetHashCode(Expression value, Expression comparer) {
      if(value == null) {
        throw new ArgumentNullException(nameof(value));
      }//if

      if(comparer != null) {
        // comparer.GetHashCode(value);
        return CallComparerMethod(comparer, GetHashCodeMethodName, value);
      } else {
        var call = Expression.Call(value, GetHashCodeDelegate.Method);
        if(value.Type.IsValueType) {
          // value.GetHashCode();
          return call;
        } else {
          // (value != null ? value.GetHashCode(); : 0
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
        return CallComparerMethod(comparer, CompareMethodName, left, right);
      } else {
        // (left < right) ? -1 : (left > right ? 1 : 0);
        return Expression.Condition(Expression.LessThan(left, right), MinusOne,
          Expression.Condition(Expression.GreaterThan(left, right), One, Zero));
      }//if
    }

    #region Replace Parameters

    private static Expression ReplaceParameter(LambdaExpression expression, Expression to) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(expression.Parameters.Count != 1) {
        throw new ArgumentException("expression.Parameters.Count != 1", nameof(expression));
      }//if

      var body = expression.Body;
      var parameter = expression.Parameters[0];
      return Replace(body, parameter, to);
    }

    private static Expression Replace(Expression expression, Expression what, Expression to) {
      var visitor = new ReplaceVisitor(what, to);
      return visitor.Visit(expression);
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

      public Expression What { get; }
      public Expression To { get; }

      public override Expression Visit(Expression node) {
        if(node == What) {
          return To;
        }//if

        return base.Visit(node);
      }
    }

    #endregion Replace Parameters

    private sealed class ComparerExpression
    {
      public ComparerExpression(LambdaExpression expresion, Expression equality = null, Expression comparison = null) {
        if(expresion == null) {
          throw new ArgumentNullException(nameof(expresion));
        }//if

        Expression = expresion;
        EqualityComparer = equality;
        Comparer = comparison;
      }

      public LambdaExpression Expression { get; }
      public Expression EqualityComparer { get; }
      public Expression Comparer { get; }

      public override string ToString() => Expression.ToString();
    }

    private static Tuple<Expression, Expression> MakeParameters(LambdaExpression expression) {
      var left = ReplaceParameter(expression, Left);
      var right = ReplaceParameter(expression, Right);
      return Tuple.Create(left, right);
    }

    #region Add Expressions

    protected virtual void AddExpression(LambdaExpression expression, Expression equalityComparer = null, Expression comparisonComparer = null) {
      var expr = new ComparerExpression(expression, equalityComparer, comparisonComparer);
      Expressions.Add(expr);
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

    public ComparerBuilder<T> AddDefault<TProperty>(Expression<Func<T, TProperty>> expression) {
      var equality = EqualityComparer<TProperty>.Default;
      var comparison = Comparer<TProperty>.Default;
      return Add(expression, equality, comparison);
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

    #endregion Add Expressions

    #region Building

    private Expression<Func<T, T, bool>> BuildEquals(LambdaExpression assert = null) {
      var items =
        from item in Expressions
        let parameters = MakeParameters(item.Expression)
        let expr = MakeEquals(parameters.Item1, parameters.Item2, item.EqualityComparer)
        select assert != null ? ApplyAssert(assert, expr, parameters.Item1, parameters.Item2) : expr;
      return AggregateEquals(items);
    }

    private Expression<Func<T, int>> BuildGetHashCode() {
      var items =
        from item in Expressions
        let parameters = MakeParameters(item.Expression)
        select MakeGetHashCode(parameters.Item1, item.EqualityComparer);
      return AggregateGetHashCode(items);
    }

    private Expression<Func<T, T, int>> BuildCompare(LambdaExpression assert = null) {
      var items =
        from item in Expressions
        let parameters = MakeParameters(item.Expression)
        let expr = MakeEquals(parameters.Item1, parameters.Item2, item.EqualityComparer)
        select assert != null ? ApplyAssert(assert, expr, parameters.Item1, parameters.Item2) : expr;
      return AggregateCompare(items);
    }

    private static Expression<Func<T, T, bool>> AggregateEquals(IEnumerable<Expression> items) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var expression = items.Aggregate((left, right) => Expression.AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : Expression.OrElse(
            ReferenceEqual(Left, Right),
            Expression.AndAlso(
              Expression.AndAlso(IsNotNull(Left), IsNotNull(Right)),
              expression));
      return Expression.Lambda<Func<T, T, bool>>(body, Left, Right);
    }

    private static Expression<Func<T, int>> AggregateGetHashCode(IEnumerable<Expression> items) {
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
        : Expression.Condition(IsNull(Left), Zero, expression);
      return Expression.Lambda<Func<T, int>>(body, Left);
    }

    private static Expression<Func<T, T, int>> AggregateCompare(IEnumerable<Expression> items) {
      if(items == null) {
        throw new ArgumentNullException(nameof(items));
      }//if

      var reverse = items.Reverse().ToList();
      Expression seed = Expression.Return(Return, reverse.First());
      var expression = reverse.Skip(1).Aggregate(seed,
        (acc, value) => Expression.IfThenElse(
          Expression.NotEqual(Expression.Assign(Compare, value), Zero), ReturnCompare, acc));
      var body = IsValueType
        ? expression
        //if((object)x == (object)y) {
        //  return 0;
        //} else if((object)x == null) {
        //  return -1;
        //} else if((object)y == null) {
        //  return 1;
        //} else {
        //  return expression;
        //}//if
        : Expression.IfThenElse(ReferenceEqual(Left, Right), ReturnZero,
            Expression.IfThenElse(IsNull(Left), ReturnMinusOne,
              Expression.IfThenElse(IsNull(Right), ReturnOne, expression)));
      var block = Expression.Block(CompareVariables, body, LabelZero);
      return Expression.Lambda<Func<T, T, int>>(block, Left, Right);
    }

    #endregion Building

    #region Builders

    private EqualityComparer<T> BuildEqualityComparer(Expression<Func<bool, bool>> assert) {
      if(!Expressions.Any()) {
        return Comparers.EmptyEqualityComparer<T>();
      }//if

      var equals = BuildEquals(assert);
      var hashCode = BuildGetHashCode();
      return Comparers.Create(equals.Compile(), hashCode.Compile());
    }

    public EqualityComparer<T> BuildEqualityComparer() {
      return BuildEqualityComparer(assert: null);
    }

    public EqualityComparer<T> BuildCheckedEqualityComparer(Expression<Func<bool, bool>> assert = null) {
      return BuildEqualityComparer(assert ?? (value => value));
    }

    private Comparer<T> BuildComparer(Expression<Func<int, bool>> assert) {
      if(!Expressions.Any()) {
        return Comparers.EmptyComparer<T>();
      }//if

      var compare = BuildCompare(assert);
      return Comparers.Create(compare.Compile());
    }

    public Comparer<T> BuildComparer() {
      return BuildComparer(null);
    }

    public Comparer<T> BuildCheckedComparer(Expression<Func<int, bool>> assert = null) {
      return BuildComparer(assert ?? (value => value != 0));
    }

    #endregion Builders

    #region IEnumerable<Expression> Members

    IEnumerator<Expression> IEnumerable<Expression>.GetEnumerator() {
      foreach(var item in Expressions) {
        yield return item.Expression;
      }//for
    }

    IEnumerator IEnumerable.GetEnumerator() {
      IEnumerable<Expression> enumerable = this;
      return enumerable.GetEnumerator();
    }

    #endregion IEnumerable<Expression> Members
  }
}
