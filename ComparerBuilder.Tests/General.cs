﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparerBuilder.Tests
{
  [TestClass]
  public sealed class General
  {
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void Run() {
      var subbuilder = new ComparerBuilder<SubData>()
        .Add(value => value.Test ?? String.Empty, StringComparer.OrdinalIgnoreCase);

      var basebuilder = new ComparerBuilder<BaseData>()
        .Add(value => value.Test1)
        .Add(value => value.Test2);

      var builder = new ComparerBuilder<Data>()
        .Add(basebuilder)
        .Add(value => value.SubData1, subbuilder)
        .Add(value => value.SubData1, subbuilder.BuildEqualityComparer(), subbuilder.BuildComparer());

      var equalityComparer = builder.BuildEqualityComparerChecked();

      var o = new object();
      var data1 = new Data(0, o, new SubData("a"));
      var data2 = new Data(0, o, new SubData("A"));
      var data2a = new Data(0, o, new SubData("c"));
      var data3 = new Data(1, null, new SubData("b"));

      var test1 = equalityComparer.Equals(data1, data2);

      try {
        var test2 = equalityComparer.Equals(data1, data2a);
      } catch (Exception ex) {

        throw;
      }
    }
  }
}

internal class BaseData
{
  public BaseData(int test1 = 0, object test2 = null) {
    Test1 = test1;
    Test2 = test2;
  }

  public int Test1 { get; }
  public object Test2 { get; }
}

internal sealed class Data : BaseData
{
  public Data(int test1 = 0, object test2 = null, SubData subData1 = null) : base(test1, test2) {
    SubData1 = subData1;
  }

  public SubData SubData1 { get; }
}

internal sealed class SubData
{
  public SubData(string test = null) {
    Test = test;
  }

  public string Test { get; }
}