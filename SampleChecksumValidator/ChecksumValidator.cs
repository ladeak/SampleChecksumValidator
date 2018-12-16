using System;
using System.Numerics;
using RapideFix;

namespace SampleChecksumValidator
{
  public class ChecksumValidator
  {
    private readonly int Modulus = 256;
    private readonly IntegerToFixConverter _converter;
    private readonly int ChecksumLength = 7;
    private readonly int ChecksumValueLength = 3;

    public ChecksumValidator(IntegerToFixConverter converter)
    {
      _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    public bool IsValid(Span<byte> data, int checksumTagStartIndex)
    {
      if(checksumTagStartIndex < 0 || (checksumTagStartIndex + ChecksumLength) != data.Length)
      {
        return false;
      }

      int sum = 0;
      for(int i = 0; i < checksumTagStartIndex; i++)
      {
        sum += data[i];
      }

      int expectedChecksum = sum % Modulus;
      Span<byte> expectedDigits = stackalloc byte[ChecksumValueLength];
      _converter.Convert(number: expectedChecksum, into: expectedDigits, count: ChecksumValueLength);

      var receivedChecksum = data.Slice(checksumTagStartIndex + 3, ChecksumValueLength);
      return receivedChecksum.SequenceEqual(expectedDigits);
    }

    public bool IsValidSimd(Span<byte> input, int checksumTagStartIndex)
    {
      if(checksumTagStartIndex < 0 || (checksumTagStartIndex + ChecksumLength) != input.Length)
      {
        return false;
      }
      var data = input.Slice(0, checksumTagStartIndex);
      int vectorLength = Vector<byte>.Count;
      Vector<ushort> sumV1 = Vector<ushort>.Zero;
      Vector<ushort> sumV2 = Vector<ushort>.Zero;
      while(data.Length >= vectorLength)
      {
        var vectorizedData = new Vector<byte>(data);
        Vector.Widen(vectorizedData, out Vector<ushort> left, out Vector<ushort> right);
        sumV1 += left;
        sumV2 += right;
        data = data.Slice(vectorLength);
      }

      int sum = 0;
      for(int i = 0; i < data.Length; i++)
      {
        sum += data[i];
      }
      sum += Vector.Dot(Vector<ushort>.One, sumV1) + Vector.Dot(Vector<ushort>.One, sumV2);

      int expectedChecksum = sum % Modulus;
      Span<byte> expectedDigits = stackalloc byte[ChecksumValueLength];
      _converter.Convert(number: expectedChecksum, into: expectedDigits, count: ChecksumValueLength);

      var receivedChecksum = input.Slice(checksumTagStartIndex + 3, ChecksumValueLength);
      return receivedChecksum.SequenceEqual(expectedDigits);
    }

    public bool IsValidSimdSlim(Span<byte> input, int checksumTagStartIndex)
    {
      if(checksumTagStartIndex < 0 || (checksumTagStartIndex + ChecksumLength) != input.Length)
      {
        return false;
      }
      var data = input.Slice(0, checksumTagStartIndex);
      int vectorLength = Vector<byte>.Count;
      Vector<byte> sumV = Vector<byte>.Zero;
      while(data.Length >= vectorLength)
      {
        sumV += new Vector<byte>(data);
        data = data.Slice(vectorLength);
      }

      byte sum = Vector.Dot(Vector<byte>.One, sumV);
      for(int i = 0; i < data.Length; i++)
      {
        sum += data[i];
      }

      Span<byte> expectedDigits = stackalloc byte[ChecksumValueLength];
      _converter.Convert(number: sum, into: expectedDigits, count: ChecksumValueLength);

      var receivedChecksum = input.Slice(checksumTagStartIndex + 3, ChecksumValueLength);
      return receivedChecksum.SequenceEqual(expectedDigits);
    }
  }
}
