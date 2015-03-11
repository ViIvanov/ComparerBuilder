using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace GBricks.Collections
{
  using static ComparerBuilder;
  using static Expression;

  [DebuggerDisplay("{DebuggerDisplay}")]
  public sealed class ComparerBuilder<T>
  {
    #region Cached Expression and Reflection objects

    internal static readonly ParameterExpression X = Parameter(typeof(T), "x");
    internal static readonly ParameterExpression Y = Parameter(typeof(T), "y");
    internal static readonly ParameterExpression Obj = Parameter(typeof(T), "obj");

    private static readonly ParameterExpression Compare = Parameter(typeof(int));
    private static readonly IEnumerable<ParameterExpression> CompareVariables = new[] { Compare, };
    private static readonly LabelTarget Return = Label(typeof(int));
    private static readonly LabelExpression LabelZero = Label(Return, Zero);
    private static readonly GotoExpression ReturnZero = Return(Return, Zero);
    private static readonly GotoExpression ReturnOne = Return(Return, One);
    private static readonly GotoExpression ReturnMinusOne = Return(Return, MinusOne);
    private static readonly GotoExpression ReturnCompare = Return(Return, Compare);

    private static readonly Func<int, int, int> RotateRightDelegate = Comparers.RotateRight;
    private static readonly Type ComparedType = typeof(T);
    private static readonly bool IsValueType = ComparedType.IsValueType;

    #endregion Cached Expression and Reflection objects

    public ComparerBuilder() { }

    private ComparerBuilder(ImmutableArray<IComparerExpression> expressions, IComparerBuilderInterception interception) {
      Expressions = expressions;
      Interception = interception;
    }

    private ImmutableArray<IComparerExpression> Expressions { get; }
    public IComparerBuilderInterception Interception { get; }
    public bool IsEmpty => Expressions.IsDefaultOrEmpty;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"Expressions: {(Expressions.IsDefaultOrEmpty ? 0 : Expressions.Length)} item(s).";

    #region Add Expressions

    private ComparerBuilder<T> Add(IComparerExpression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var expressions = Expressions.IsDefaultOrEmpty ? ImmutableArray.Create(expression) : Expressions.Add(expression);
      return new ComparerBuilder<T>(expressions, Interception);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) {
      return Add(expression, null, null, filePath, lineNumber);
    }

    public ComparerBuilder<T> Add<TProperty>(Expression<Func<T, TProperty>> expression, IEqualityComparer<TProperty> equalityComparer, IComparer<TProperty> comparisonComparer, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) {
      var equality = equalityComparer ?? EqualityComparer<TProperty>.Default;
      var comparison = comparisonComparer ?? Comparer<TProperty>.Default;
      var value = new ComparerExpression<TProperty>(expression, equalityComparer, comparisonComparer, filePath, lineNumber);
      return Add(value);
    }

    public ComparerBuilder<T> Add<TProperty, TComparer>(Expression<Func<T, TProperty>> expression, TComparer comparer, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0) where TComparer : IEqualityComparer<TProperty>, IComparer<TProperty> {
      if(comparer == null) {
        throw new ArgumentNullException(nameof(comparer));
      }//if

      return Add(expression, comparer, comparer, filePath, lineNumber);
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

    public ComparerBuilder<T> WithInterception(IComparerBuilderInterception value) {
      if(value != Interception) {
        return new ComparerBuilder<T>(Expressions, value);
      } else {
        return this;
      }//if
    }

    #endregion Add Expressions

    #region Build Methods

    internal Expression<Func<T, T, bool>> BuildEquals(ParameterExpression x, ParameterExpression y, Type comparedType, IComparerBuilderInterception interception = null) {
      var expression = Expressions.Select(item => item.AsEquals(x, y, comparedType, interception)).Aggregate((left, right) => AndAlso(left, right));
      var body = IsValueType
        ? expression
        // (object)x == (object)y || ((object)x != null && (object)y != null && expression);
        : OrElse(ReferenceEqual(x, y), AndAlso(AndAlso(IsNotNull(x), IsNotNull(y)), expression));
      return Lambda<Func<T, T, bool>>(body, x, y);
    }

    internal Expression<Func<T, int>> BuildGetHashCode(ParameterExpression obj, Type comparedType, IComparerBuilderInterception interception = null) {
      var list = Expressions.Select(item => item.AsGetHashCode(obj, comparedType, interception)).ToList();
      var expression = list.Skip(1).Select((item, index) => Tuple.Create(item, index + 1))
        .Aggregate(list.First(), (acc, item) => ExclusiveOr(acc, Call(RotateRightDelegate.Method, item.Item1, Constant(item.Item2))));
      var body = IsValueType
        ? expression
        // ((object)obj == null) ? 0 : expression;
        : Condition(IsNull(obj), Zero, expression);
      return Lambda<Func<T, int>>(body, obj);
    }

    internal Expression<Func<T, T, int>> BuildCompare(ParameterExpression x, ParameterExpression y, Type comparedType, IComparerBuilderInterception interception = null) {
      var reverse = Expressions.Select(item => item.AsCompare(x, y, comparedType, interception)).Reverse().ToList();
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

    private EqualityComparer<T> CreateEqualityComparer(IComparerBuilderInterception interception = null) {
      ThrowIfEmpty();
      var equals = BuildEquals(X, Y, ComparedType, interception);
      var hashCode = BuildGetHashCode(Obj, ComparedType, interception);
      return Comparers.Create(equals.Compile(), hashCode.Compile());
    }

    public EqualityComparer<T> CreateEqualityComparer() {
      return CreateEqualityComparer(interception: null);
    }

    public EqualityComparer<T> CreateEqualityComparerIntercepted(IComparerBuilderInterception interception = null) {
      return CreateEqualityComparer(interception ?? Interception ?? DefaultInterception.Instance);
    }

    private Comparer<T> CreateComparer(IComparerBuilderInterception interception = null) {
      ThrowIfEmpty();
      var compare = BuildCompare(X, Y, ComparedType, interception);
      return Comparers.Create(compare.Compile());
    }

    public Comparer<T> CreateComparer() {
      return CreateComparer(interception: null);
    }

    public Comparer<T> CreateComparerIntercepted(IComparerBuilderInterception interception = null) {
      return CreateComparer(interception ?? Interception ?? DefaultInterception.Instance);
    }

    #endregion Build Comparers
  }
}
