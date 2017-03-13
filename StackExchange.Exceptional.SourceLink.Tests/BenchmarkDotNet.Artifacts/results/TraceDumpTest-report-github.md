``` ini

BenchmarkDotNet=v0.10.3.0, OS=Microsoft Windows NT 6.2.9200.0
Processor=Intel(R) Core(TM) i7-5960X CPU 3.00GHz, ProcessorCount=16
Frequency=2928835 Hz, Resolution=341.4327 ns, Timer=TSC
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1586.0
  DefaultJob : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1586.0


```
 |           Method |       Mean |    StdDev |
 |----------------- |----------- |---------- |
 | NormalStackTrace | 46.3923 us | 0.2883 us |
 |  FancyStackTrace | 40.8199 us | 0.1590 us |
