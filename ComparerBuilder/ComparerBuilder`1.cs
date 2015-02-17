using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ComparerBuilder
{
  public class ComparerBuilder<T>
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

    public ComparerBuilder() : this(false) { }

    public ComparerBuilder(bool @checked) {
      IsCheckedMode = @checked;

      EqualsExpressions = new List<Expression>();
      GetHashCodeExpressions = new List<Expression>();
      CompareExpressions = new List<Expression>();
    }

    public bool IsCheckedMode { get; }

    private IList<Expression> EqualsExpressions { get; }
    private IList<Expression> GetHashCodeExpressions { get; }
    private IList<Expression> CompareExpressions { get; }

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

    protected Expression WrapCheckedCall(Expression expression, bool equal, Expression check, Expression left, Expression right) {
      return IsCheckedMode ? MakeCheckedCall(expression, equal, check, left, right) : expression;
    }

    protected virtual Expression MakeCheckedCall(Expression expression, bool equal, Expression check, Expression left, Expression right) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(check == null) {
        throw new ArgumentNullException(nameof(check));
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
      var compare = equal ? Expression.Equal(assign, check) : Expression.NotEqual(assign, check);
      var test = Expression.IfThen(compare, @throw);
      return Expression.Block(result.Type, new[] { result, }, test, result);
    }

    protected Expression MakeEquals(Expression left, Expression right, Expression comparer) {
      var equals = MakeEqualsCore(left, right, comparer);
      return WrapCheckedCall(equals, true, False, left, right);
    }

    protected virtual Expression MakeEqualsCore(Expression left, Expression right, Expression comparer) {
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

    protected virtual Expression MakeGetHashCode(Expression value, Expression comparer) {
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

    protected Expression MakeCompare(Expression left, Expression right, Expression comparer) {
      var compare = MakeCompareCore(left, right, comparer);
      return WrapCheckedCall(compare, false, Zero, left, right);
    }

    protected virtual Expression MakeCompareCore(Expression left, Expression right, Expression comparer) {
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

    private void AddEqualityCore(Tuple<Expression, Expression> args, Expression comparer) {
      if(args == null) {
        throw new ArgumentNullException(nameof(args));
      }//if

      var equals = MakeEquals(args.Item1, args.Item2, comparer);
      EqualsExpressions.Add(equals);

      var hash = MakeGetHashCode(args.Item1, comparer);
      GetHashCodeExpressions.Add(hash);
    }

    private void AddComparisonCore(Tuple<Expression, Expression> args, Expression comparer) {
      if(args == null) {
        throw new ArgumentNullException(nameof(args));
      }//if

      var compare = MakeCompare(args.Item1, args.Item2, comparer);
      CompareExpressions.Add(compare);
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

    private static Tuple<Expression, Expression> Parameters(LambdaExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(expression.Parameters.Count != 1) {
        throw new ArgumentException("expression.Parameters.Count != 1", nameof(expression));
      }//if

      var left = new ReplaceVisitor(expression.Parameters[0], Left);
      var leftReplaced = left.Visit(expression.Body);
      var right = new ReplaceVisitor(expression.Parameters[0], Right);
      var rightReplaced = right.Visit(expression.Body);
      return Tuple.Create(leftReplaced, rightReplaced);
    }

    public ComparerBuilder<T> Add(LambdaExpression expression, Expression equality = null, Expression comparison = null) {
      var args = Parameters(expression);
      AddEqualityCore(args, equality);
      AddComparisonCore(args, comparison);
      return this;
    }

    public ComparerBuilder<T> AddDefault(LambdaExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException("expression");
      }

      var type = expression.ReturnType;
      var equality = typeof(EqualityComparer<>).MakeGenericType(type).GetProperty(nameof(EqualityComparer<T>.Default)).GetValue(null, null);
      var comparison = typeof(Comparer<>).MakeGenericType(type).GetProperty(nameof(Comparer<T>.Default)).GetValue(null, null);
      return Add(expression, Expression.Constant(equality), Expression.Constant(comparison));
    }

    public ComparerBuilder<T> AddEquality(LambdaExpression expression, Expression equality = null) {
      var args = Parameters(expression);
      AddEqualityCore(args, equality);
      return this;
    }

    public ComparerBuilder<T> AddComparison(LambdaExpression expression, Expression comparison = null) {
      var args = Parameters(expression);
      AddComparisonCore(args, comparison);
      return this;
    }

    public ComparerBuilder<T> Add<P>(Expression<Func<T, P>> expression, IEqualityComparer<P> equality = null, IComparer<P> comparison = null) {
      return Add(expression, NullableConstant(equality), NullableConstant(comparison));
    }

    public ComparerBuilder<T> Add<P, C>(Expression<Func<T, P>> expression, C comparer) where C : IEqualityComparer<P>, IComparer<P> {
      var constant = NullableConstant(comparer);
      return Add(expression, constant, constant);
    }

    public ComparerBuilder<T> AddEquality<P>(Expression<Func<T, P>> expression, IEqualityComparer<P> equality = null) {
      return AddEquality(expression, NullableConstant(equality));
    }

    public ComparerBuilder<T> AddComparison<P>(Expression<Func<T, P>> expression, IComparer<P> comparison = null) {
      return AddComparison(expression, NullableConstant(comparison));
    }

    public ComparerBuilder<T> AddDefault<P>(Expression<Func<T, P>> expression) {
      return Add(expression, EqualityComparer<P>.Default, Comparer<P>.Default);
    }

    private static Expression<Func<T, T, bool>> BuildEquals(IEnumerable<Expression> items) {
      if(items == null) {
        throw new ArgumentNullException("items");
      }

      var expression = items.Aggregate(Expression.AndAlso);
      var body = IsValueType
        ? expression
        // return (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : Expression.OrElse(
            ReferenceEqual(Left, Right),
            Expression.AndAlso(
              Expression.AndAlso(IsNotNull(Left), IsNotNull(Right)),
              expression));
      return Expression.Lambda<Func<T, T, bool>>(body, Left, Right);
    }

    private static Expression<Func<T, int>> BuildGetHashCode(IEnumerable<Expression> items) {
      if(items == null) {
        throw new ArgumentNullException("items");
      }

      var list = items as ICollection<Expression> ?? items.ToList();
      var expression = list.Skip(1).Select((item, index) => Tuple.Create(item, index + 1))
        .Aggregate(list.First(), (acc, item) =>
          Expression.ExclusiveOr(acc,
            Expression.Call(RotateRightDelegate.Method, item.Item1, Expression.Constant(item.Item2))));
      var body = IsValueType
        ? expression
        // return ((object)x == null) ? 0 : expression;
        : Expression.Condition(IsNull(Left), Zero, expression);
      return Expression.Lambda<Func<T, int>>(body, Left);
    }

    private static Expression<Func<T, T, int>> BuildCompare(IEnumerable<Expression> items) {
      if(items == null) {
        throw new ArgumentNullException("items");
      }

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

    public EqualityComparer<T> ToEqualityComparer() {
      Debug.Assert(EqualsExpressions.Count == GetHashCodeExpressions.Count, "EqualsExpressions.Count == GetHashCodeExpressions.Count",
          "EqualsExpressions.Count == {0}, GetHashCodeExpressions.Count == {1}", EqualsExpressions.Count, GetHashCodeExpressions.Count);

      if(EqualsExpressions.Count == 0 || GetHashCodeExpressions.Count == 0) {
        return Comparers.EmptyEqualityComparer<T>();
      }

      var equals = BuildEquals(EqualsExpressions);
      var hashCode = BuildGetHashCode(GetHashCodeExpressions);
      return Comparers.Create(equals.Compile(), hashCode.Compile());
    }

    public Comparer<T> ToComparer() {
      if(CompareExpressions.Count == 0) {
        return Comparers.EmptyComparer<T>();
      }

      var compare = BuildCompare(CompareExpressions);
      return Comparers.Create(compare.Compile());
    }
  }
}
