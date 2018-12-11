using BenchmarkDotNet.Running;

namespace SampleChecksumValidator
{
  public class Program
  {
    public static void Main(string[] args)
    {
      var summary = BenchmarkRunner.Run<ChecksumValidatorBenchmark>();
      //string Sample = "35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
      //var _message = new TestFixMessageBuilder(Sample).Build(out int checksumValue, out int checksumStart);
      //var _checkSumStart = checksumStart;
      //var _validator = new ChecksumValidator(IntegerToFixConverter.Instance);

      //Console.WriteLine(_validator.IsValidSimdSlim(_message, _checkSumStart));
    }
  }
}
