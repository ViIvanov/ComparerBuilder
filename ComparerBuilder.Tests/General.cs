using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GBricks.Collections;

namespace ComparerBuilder.Tests
{
  [TestClass]
  public sealed class General
  {
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void Run() {
      var builderSubData = new ComparerBuilder<SubData>()
        .Add(value => value.Test ?? String.Empty, StringComparer.OrdinalIgnoreCase);

      var builderBaseDataByTest1 = new ComparerBuilder<BaseData>()
        .Add(value => value.Test1 % 2);

      var builderBaseDataByTest2 = new ComparerBuilder<BaseData>()
        .Add(value => value.Test2 != null ? value.Test2.Value.Date : default(DateTime));

      var builderData = builderBaseDataByTest1
        .Add(builderBaseDataByTest2)
        .AsDerived<Data>()
        .Add(value => value.SubData1, builderSubData);

      var equalityComparer = builderData.CreateEqualityComparer();
      var comparer = builderData.CreateComparer();

      var data1 = new Data(2, DateTime.Now, new SubData("a"));
      var data2 = new Data(4, DateTime.Now, new SubData("A"));
      var data3 = new Data(6, DateTime.Now, new SubData("c"));
      var data4 = new Data(1, null, new SubData("b"));

      var test1 = equalityComparer.Equals(data1, data2); // True
      var test2 = equalityComparer.Equals(data2, data3); // False
      var test3 = comparer.Compare(data1, data4); // -1

      try {
        var xtest2 = equalityComparer.Equals(data1, data4);
      } catch (Exception) {

        throw;
      }
    }
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