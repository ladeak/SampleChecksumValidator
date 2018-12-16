## Vectors in .Net

A couple of months back I wrote about [Allocation free algorithms](https://ladeak.wordpress.com/2018/07/14/allocation-free-algorithms), and took a look at an example of calculation the checksum of FIX messages. The end results are utilizing the latest C# 7.3 language features.

For many reasons that implementation seems a reasonable algorithm and runs quite ok and without managed heap allocations. However, it could be made even faster, and in this post, I will take a look how. I will use [Vectors](https://docs.microsoft.com/en-us/dotnet/api/system.numerics.vector-1?view=netcore-2.2) which is available in the [System.Numerics](https://docs.microsoft.com/en-us/dotnet/api/system.numerics?view=netcore-2.2) namespace. 

```Vector<T>``` provides an abstractions over data, to enable data parallelism known as [Single Instruction Multiple Data](https://en.wikipedia.org/wiki/SIMD) (SIMD). The nice thing about Vector<T> and the CLR itself, that it can leverage the SIMD capability of the processor itself, thus providing access to low-level hardware acceleration in a managed language. 

### The Baseline Algorithm

Let me begin with the original algorithm. The ```IsValid``` method receives a ```Span<T>``` of input data to be validated and an integer named *checksumTagStartIndex* which indicates the position of the checksum tag appearing in the data.

> The checksum tag is the last tag of a [FIX](https://en.wikipedia.org/wiki/Financial_Information_eXchange#Trailer:_Checksum) messages. Its tag is 10 and the value is a 3-digit integer. The value is calculated by summing the byte values from the beginning of the message up to the checksum tag and taking a modulo of 256 operation on the calculated value. This value is then represented as a 3-digit integer encoded with ASCII.

The method first validates that the *checksumTagStartIndex* given is really the last tag of the input data. Then it uses a simple loop to sum the bytes up to the checksum tag. It calculates the modulo operation, and it converts the representation of the result using only the stack. Finally, it compares the given input checksum with the calculated one.

```csharp
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

```
### Naive Vectorization

In the first attempt, I will show a naive vectorization. The goal is to increase the throughput of the ```for``` loop from the previous solution. If we could sum multiple byte at the same time, we could improve the performance significantly. The code looks a little more complicated at first sight:

```csharp
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
```

The beginning and the end of the code looks the same, while the sum calculation has changed. First, we will need to look at how many bytes can the given hardware store in its SIMD register ```Vector<byte>.Count```. Then we initialize two unsigned shorts vectors of zeros: *sumV1* and *sumV2*. In the while loop we can read *vectorLength* of bytes from the input data to a temporary vector. Then we [Widen](https://docs.microsoft.com/en-us/dotnet/api/system.numerics.vector.widen?view=netcore-2.2) these bytes to ushorts: this operation says that we want to handle the bytes as ushorts (like an up cast). As [ushort](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/ushort) is a 16-bit value, a vector could only handle half as many values as with the 8-bit bytes. So, the output is 2 ```Vector<ushort>``` named left and right. The rest of the while loop adds the values to the cumulative variables and slices the next part of the input message. The second for loop is needing to sum the last slice of data, that is smaller than *vectorLength*. Finally, to have a single sum value we can use the Dot product of a *one* Vector and the cumulative values.


#### Benchmark

Let's compare the two approaches. I use [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) to benchmark both methods. I use 3 sample fix messages:

```csharp
Sample0 = "35=8|49=PHLX|20=3|167=CS|54=1|38=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
Sample1 = "35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
Sample2 = "35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|35=8|49=PHLX|56=PERS|52=20071123-05:30:00.000|11=ATOMNOCCC9990900|20=3|150=E|39=E|55=MSFT|167=CS|54=1|38=15|40=2|44=15|58=PHLX EQUITY TESTING|59=0|47=C|32=0|31=0|151=15|14=0|6=0|";
```

and the two methods benchmarked:
```csharp
[Benchmark(Baseline = true, Description = "Regular")]
public void Validate() => _validator.IsValid(_message, _checksumStart);

[Benchmark(Description = "SIMD")]
public void ValidateSIMD() => _validator.IsValidSimd(_message, _checksumStart);
```

The results indicate that the for the shortest message the original approach is slighlty faster, but for the largest message, there is already a 33% improvement:

``` ini
BenchmarkDotNet=v0.11.3, OS=Windows 10.0.17134.471 (1803/April2018Update/Redstone4), VM=Hyper-V
Intel Xeon CPU E5-2673 v3 2.40GHz, 1 CPU, 2 logical and 2 physical cores
.NET Core SDK=2.2.100
  [Host] : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT
  Core   : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT

Job=Core  Runtime=Core  
```
|  Method |          Sample |     Mean |    Error |   StdDev | Ratio | RatioSD |
|-------- |---------------- |---------:|---------:|---------:|------:|--------:|
| Regular |  35=8(...) [95] | 116.3 ns | 2.013 ns | 1.883 ns |  1.00 |    0.00 |
|    SIMD |  35=8(...) [95] | 133.3 ns | 2.638 ns | 3.037 ns |  1.14 |    0.03 |
|         |                 |          |          |          |       |         |
| Regular | 35=8(...) [178] | 167.5 ns | 3.274 ns | 4.141 ns |  1.00 |    0.00 |
|    SIMD | 35=8(...) [178] | 140.2 ns | 2.723 ns | 2.674 ns |  0.84 |    0.02 |
|         |                 |          |          |          |       |         |
| Regular | 35=8(...) [356] | 274.4 ns | 4.755 ns | 4.448 ns |  1.00 |    0.00 |
|    SIMD | 35=8(...) [356] | 182.9 ns | 4.034 ns | 4.317 ns |  0.67 |    0.02 |

### Vectorizing V2

The improvement for the naive vectorization is clear for long messages, but can we still improve it? Giving it a second thought the previous vectorized algorithm has a limitation: *ushort*-s can represent values up to 65,535. What happens if the message is just too long, so a ushort is not big enough to represent the local sum? The variable will overflow. But we get lucky here, because 256\*256=65536 and 256 is just the modulus we use later on. This means we don't even need to use *ushort*-s or *int*-s, we can simply use bytes if we can accept overflow.

This brings us to the following implementation:

```csharp
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
```
The beginning and the end of this method is still quite the same, and the middle is similar too. The difference to the previous solution is that we can use purely ```Vector<byte>```-s, thus we can parallelize more data at the same time, and we can omit the Widen method call. This method is even simpler and faster.

#### Benchmark V2

Adding the new ```IsValidSimdSlim``` to the benchmark:

```csharp
[Benchmark(Description = "Slim")]
public void ValidateSIMDSlim() => _validator.IsValidSimdSlim(_message, _checksumStart);
```

The results, indicate further improvement, especially for large messages, where the new approach is nearly twice as fast:

``` ini

BenchmarkDotNet=v0.11.3, OS=Windows 10.0.17134.471 (1803/April2018Update/Redstone4), VM=Hyper-V
Intel Xeon CPU E5-2673 v3 2.40GHz, 1 CPU, 2 logical and 2 physical cores
.NET Core SDK=2.2.100
  [Host] : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT
  Core   : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT

Job=Core  Runtime=Core  

```
|  Method |          Sample |     Mean |    Error |    StdDev |   Median | Ratio | RatioSD |
|-------- |---------------- |---------:|---------:|----------:|---------:|------:|--------:|
| Regular |  35=8(...) [95] | 115.3 ns | 2.307 ns |  2.369 ns | 115.8 ns |  1.00 |    0.00 |
|    SIMD |  35=8(...) [95] | 128.9 ns | 2.596 ns |  3.465 ns | 127.8 ns |  1.12 |    0.04 |
|    Slim |  35=8(...) [95] | 113.8 ns | 2.355 ns |  2.203 ns | 113.3 ns |  0.99 |    0.03 |
|         |                 |          |          |           |          |       |         |
| Regular | 35=8(...) [178] | 167.8 ns | 3.191 ns |  2.985 ns | 167.2 ns |  1.00 |    0.00 |
|    SIMD | 35=8(...) [178] | 151.0 ns | 5.500 ns | 16.218 ns | 144.1 ns |  0.84 |    0.03 |
|    Slim | 35=8(...) [178] | 135.6 ns | 2.320 ns |  2.057 ns | 136.1 ns |  0.81 |    0.02 |
|         |                 |          |          |           |          |       |         |
| Regular | 35=8(...) [356] | 280.0 ns | 5.463 ns |  5.366 ns | 279.8 ns |  1.00 |    0.00 |
|    SIMD | 35=8(...) [356] | 184.2 ns | 3.051 ns |  2.854 ns | 184.2 ns |  0.66 |    0.02 |
|    Slim | 35=8(...) [356] | 158.5 ns | 3.115 ns |  3.587 ns | 157.6 ns |  0.56 |    0.02 |

Finally, I ran a test on different machine as well, which is my current Surface Pro. The results of the benchmark are attached below. We can see that with this Intel processor the performance gain is even bigger.

```ini

BenchmarkDotNet=v0.11.3, OS=Windows 10.0.17763.194 (1809/October2018Update/Redstone5)
Intel Core i5-6300U CPU 2.40GHz (Skylake), 1 CPU, 4 logical and 2 physical cores
.NET Core SDK=2.2.101
  [Host] : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT
  Core   : .NET Core 2.2.0 (CoreCLR 4.6.27110.04, CoreFX 4.6.27110.04), 64bit RyuJIT

Job=Core  Runtime=Core  
```
|  Method |          Sample |      Mean |     Error |    StdDev | Ratio |
|-------- |---------------- |----------:|----------:|----------:|------:|
| Regular |  35=8(...) [95] | 112.47 ns | 0.5235 ns | 0.4897 ns |  1.00 |
|    SIMD |  35=8(...) [95] |  97.69 ns | 0.2650 ns | 0.2349 ns |  0.87 |
|    Slim |  35=8(...) [95] |  81.47 ns | 0.2168 ns | 0.2028 ns |  0.72 |
|         |                 |           |           |           |       |
| Regular | 35=8(...) [178] | 170.35 ns | 0.5023 ns | 0.4698 ns |  1.00 |
|    SIMD | 35=8(...) [178] | 104.95 ns | 0.2345 ns | 0.2193 ns |  0.62 |
|    Slim | 35=8(...) [178] |  91.37 ns | 0.1781 ns | 0.1666 ns |  0.54 |
|         |                 |           |           |           |       |
| Regular | 35=8(...) [356] | 283.11 ns | 1.0103 ns | 0.9450 ns |  1.00 |
|    SIMD | 35=8(...) [356] | 137.52 ns | 0.4837 ns | 0.4288 ns |  0.49 |
|    Slim | 35=8(...) [356] | 116.93 ns | 0.3978 ns | 0.3526 ns |  0.41 |

### Source

The full [source code](https://github.com/ladeak/SampleChecksumValidator) is available on GitHub.

