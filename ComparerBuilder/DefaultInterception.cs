using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GBricks.Collections
{
  internal sealed class DefaultInterception : IComparerBuilderInterception
  {
    private DefaultInterception() { }

    public static IComparerBuilderInterception Instance { get; } = new DefaultInterception();

    private void Intercept<TValue, T>(TValue value, T first, T second, IComparerBuilderInterceptionArgs<T> args, bool hasSecond, [CallerMemberName] string memberName = null) {
      var parameters = hasSecond ? $"{first}, {second}" : $"{first}";
      Debug.Print($"{args.FilePath} ({args.LineNumber}) : {memberName}({parameters}) returned {value} for {{{args.Expression}}}");
    }

    public bool InterceptEquals<T>(bool value, T x, T y, IComparerBuilderInterceptionArgs<T> args) {
      if(!value) {
        Intercept(value, x, y, args, hasSecond: true);
      }//if

      return value;
    }

    public int InterceptGetHashCode<T>(int value, T obj, IComparerBuilderInterceptionArgs<T> args) {
      if(value == 0) {
        Intercept(value, obj, default(T), args, hasSecond: false);
      }//if

      return value;
    }

    public int InterceptCompare<T>(int value, T x, T y, IComparerBuilderInterceptionArgs<T> args) {
      if(value != 0) {
        Intercept(value, x, y, args, hasSecond: true);
      }//if

      return value;
    }
  }
}
