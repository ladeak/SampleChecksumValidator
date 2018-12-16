using System;
using BenchmarkDotNet.Running;
using RapideFix;
using RapideFix.MessageBuilders;

namespace SampleChecksumValidator
{
  public class Program
  {
    public static void Main(string[] args)
    {
      var summary = BenchmarkRunner.Run<ChecksumValidatorBenchmark>();
    }

    private static void TestOneExecution()
    {
      string sample = ChecksumValidatorBenchmark.Sample0;
      var message = new MessageBuilder().AddRaw(sample).Build();
      var checkSumStart = message.Length - ChecksumValidatorBenchmark.ChecksumLength;
      var validator = new ChecksumValidator(IntegerToFixConverter.Instance);
      Console.WriteLine(validator.IsValidSimdSlim(message, checkSumStart));
    }
  }
}
