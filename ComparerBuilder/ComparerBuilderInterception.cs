namespace GBricks.Collections
{
  public abstract class ComparerBuilderInterception
  {
    public virtual bool InterceptEquals<T>(bool value, T x, T y, ComparerBuilderInterceptionArgs<T> args) => value;
    public virtual int InterceptGetHashCode<T>(int value, T obj, ComparerBuilderInterceptionArgs<T> args) => value;
    public virtual int InterceptCompare<T>(int value, T x, T y, ComparerBuilderInterceptionArgs<T> args) => value;
  }
}
