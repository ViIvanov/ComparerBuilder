using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace GBricks.Collections
{
  internal sealed class DefaultInterception : ComparerBuilderInterception
  {
    private DefaultInterception() { }

    public static ComparerBuilderInterception Instance { get; } = new DefaultInterception();

    private void Intercept<TValue, T>(Expression expression, TValue value, T x, T y, SourceInfo sourceInfo, [CallerMemberName] string memberName = null) {
      Debug.Print($"{sourceInfo} : {memberName}({x}, {y}) returned {value} for {{{expression}}}");
    }

    public override bool InterceptEquals<T>(Expression expression, bool value, T x, T y, IEqualityComparer<T> comparer, SourceInfo sourceInfo) {
      if(!value) {
        Intercept(expression, value, x, y, sourceInfo);
      }//if

      return base.InterceptEquals(expression, value, x, y, comparer, sourceInfo);
    }

    public override int InterceptGetHashCode<T>(Expression expression, int value, T obj, IEqualityComparer<T> comparer, SourceInfo sourceInfo) {
      if(value == 0) {
        Debug.Print($"InterceptGetHashCode({obj}) returned {value} for {{{expression}}} at {sourceInfo}");
      }//if

      return base.InterceptGetHashCode(expression, value, obj, comparer, sourceInfo);
    }

    public override int InterceptCompare<T>(Expression expression, int value, T x, T y, IComparer<T> comparer, SourceInfo sourceInfo) {
      if(value != 0) {
        Intercept(expression, value, x, y, sourceInfo);
      }//if

      return base.InterceptCompare(expression, value, x, y, comparer, sourceInfo);
    }
  }
}
