namespace GBricks.Collections
{
  public interface IComparerBuilderInterception
  {
    bool InterceptEquals<T>(bool value, T x, T y, ComparerBuilderInterceptionArgs<T> args);
    int InterceptGetHashCode<T>(int value, T obj, ComparerBuilderInterceptionArgs<T> args);
    int InterceptCompare<T>(int value, T x, T y, ComparerBuilderInterceptionArgs<T> args);
  }
}
