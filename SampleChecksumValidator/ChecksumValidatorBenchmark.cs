using BenchmarkDotNet.Attributes;
using RapideFix;
using RapideFix.MessageBuilders;

namespace SampleChecksumValidator
{
  [CoreJob]
  [MarkdownExporter, HtmlExporter]
  public class ChecksumValidatorBenchmark
  {
    private const string Sample0 = "35=8|49=PHLX|20=3|167=CS|54=1|38=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
    private const string Sample1 = "35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
    private const string Sample2 = "35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";

    private ChecksumValidator _validator;
    private byte[] _message;
    private int _checkSumStart = 0;

    [Params(Sample0, Sample1, Sample2)]
    public string Sample { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
      _message = new MessageBuilder(Sample).Build(out int checksumValue, out int checksumStart);
      _checkSumStart = checksumStart;
      _validator = new ChecksumValidator(IntegerToFixConverter.Instance);

      _validator.IsValid(_message, _checkSumStart);
    }

    [Benchmark(Baseline = true, Description = "Regular")]
    public void Validate() => _validator.IsValid(_message, _checkSumStart);

    [Benchmark(Description = "SIMD")]
    public void ValidateSIMD() => _validator.IsValidSimd(_message, _checkSumStart);

    [Benchmark(Description = "Slim")]
    public void ValidateSIMDSlim() => _validator.IsValidSimdSlim(_message, _checkSumStart);
  }
}
