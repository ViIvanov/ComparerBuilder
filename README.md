#### `ComparerBuilder<T>`
Class for building equality comparers and sort comparers.

`ComparerBuilder<>` helps to compare a complex types. It can create an `EqualityComparer<>` and/or a `Comparer<>` objects for you types. You just need to "add" expressions, that describes what data do you want to compare and, optionally, comparers - how to compare that data.

Also, you can find reasons for making `ComparerBuilder<>` in this old post on [RSDN](http://rsdn.ru/forum/src/3914421.1) (in Russian).

Now `ComparerBuilder<>` rewrited on C# 6.0. You can compose a comparer builder with other comparer builders. Also, it supports "checked" comparers, that throws an exception when `IEqualityComparer<>::Equals` returns `false` or `IComparer<>::Comparer` returns not 0. "Checked" comparers are helpful in a debugging scenarios.

```cs
static class Example
{
  private static void Main() {
    var builderSubData = new ComparerBuilder<SubData>()
      .Add(value => value.Test ?? String.Empty, StringComparer.OrdinalIgnoreCase);

    var builderBaseData = new ComparerBuilder<BaseData>()
      .Add(value => value.Test1 % 2)
      .Add(value => value.Test2 != null ? value.Test2.Value.Date : default(DateTime));

    var builderData = new ComparerBuilder<Data>()
      .Add(builderBaseData)
      .Add(value => value.SubData1, builderSubData);

    var equalityComparer = builderData.BuildEqualityComparer();
    var comparer = builderData.BuildComparer();

    var data1 = new Data(2, DateTime.Now, new SubData("a"));
    var data2 = new Data(4, DateTime.Now, new SubData("A"));
    var data3 = new Data(6, DateTime.Now, new SubData("c"));
    var data4 = new Data(1, null, new SubData("b"));

    var test1 = equalityComparer.Equals(data1, data2); // True, but may be False
    var test2 = equalityComparer.Equals(data2, data3); // False
    var test3 = comparer.Compare(data1, data4); // -1
  }
}

class BaseData
{
  public BaseData(int test1 = 0, DateTime? test2 = null) {
    Test1 = test1;
    Test2 = test2;
  }

  public int Test1 { get; }
  public DateTime? Test2 { get; }
}

class Data : BaseData
{
  public Data(int test1 = 0, DateTime? test2 = null, SubData subData1 = null) : base(test1, test2) {
    SubData1 = subData1;
  }

  public SubData SubData1 { get; }
}

class SubData
{
  public SubData(string test = null) {
    Test = test;
  }

  public string Test { get; }
}
```
