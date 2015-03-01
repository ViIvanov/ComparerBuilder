using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GBricks.Collections
{
  internal sealed class DefaultInterception : ComparerBuilderInterception
  {
    private DefaultInterception() { }

    public static ComparerBuilderInterception Instance { get; } = new DefaultInterception();

    private void Intercept<TValue, T>(TValue value, T first, T second, ComparerBuilderInterceptionArgs<T> args, bool hasSecond, [CallerMemberName] string memberName = null) {
      var parameters = hasSecond ? $"{first}, {second}" : $"{first}";
      Debug.Print($"{args.FilePath} ({args.LineNumber}) : {memberName}({parameters}) returned {value} for {{{args.Expression}}}");
    }

    public override bool InterceptEquals<T>(bool value, T x, T y, ComparerBuilderInterceptionArgs<T> args) {
      if(!value) {
        Intercept(value, x, y, args, hasSecond: true);
      }//if

      return base.InterceptEquals(value, x, y, args);
    }

    public override int InterceptGetHashCode<T>(int value, T obj, ComparerBuilderInterceptionArgs<T> args) {
      if(value == 0) {
        Intercept(value, obj, default(T), args, hasSecond: false);
      }//if

      return base.InterceptGetHashCode(value, obj, args);
    }

    public override int InterceptCompare<T>(int value, T x, T y, ComparerBuilderInterceptionArgs<T> args) {
      if(value != 0) {
        Intercept(value, x, y, args, hasSecond: true);
      }//if

      return base.InterceptCompare(value, x, y, args);
    }
  }
}
