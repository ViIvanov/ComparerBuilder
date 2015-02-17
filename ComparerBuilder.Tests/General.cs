using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparerBuilder.Tests
{
  [TestClass]
  public class General
  {
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void Run() {
      var builder = new ComparerBuilder<Data>() {
        value => value.Test1,
        value => value.Test2,
      };

      var equalityComparer = builder.BuildCheckedEqualityComparer();

      var data1 = new Data(0, null);
      var data2 = new Data(0, null);
      var data3 = new Data(1, null);

      var test1 = equalityComparer.Equals(data1, data2);

      try {
        var test2 = equalityComparer.Equals(data1, data3);
      } catch (Exception ex) {

        throw;
      }
    }

    private sealed class Data
    {
      public Data(int test1, object test2) {
        Test1 = test1;
        Test2 = test2;
      }

      public int Test1 { get; }
      public object Test2 { get; }
    }
  }
}
