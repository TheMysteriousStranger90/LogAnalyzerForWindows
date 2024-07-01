using System;

namespace LogAnalyzerForWindows.Filter.Interfaces;

public interface ITimeProvider
{
    DateTime GetCurrentTime();
}