namespace GBricks.Collections
{
  public interface IComparerBuilderInterception
  {
    bool InterceptEquals<T>(bool value, T x, T y, IComparerBuilderInterceptionArgs<T> args);
    int InterceptGetHashCode<T>(int value, T obj, IComparerBuilderInterceptionArgs<T> args);
    int InterceptCompare<T>(int value, T x, T y, IComparerBuilderInterceptionArgs<T> args);
  }
}
