﻿using System;
using System.IO;

namespace Disruptor.PerfTests;

public class Program
{
    public static void Main(string[] args)
    {
        if (!ProgramOptions.TryParse(args, out var options))
        {
            ProgramOptions.PrintUsage();
            return;
        }

        if (!options.Validate())
            return;

        var selector = new PerfTestTypeSelector(options);
        var testTypes = selector.GetPerfTestTypes();

        foreach (var testType in testTypes)
        {
            RunTestForType(testType, options);
        }
    }

    private static void RunTestForType(Type perfTestType, ProgramOptions options)
    {
        var outputDirectoryPath = Path.Combine(AppContext.BaseDirectory, "results");
        if (options.GenerateReport)
            Directory.CreateDirectory(outputDirectoryPath);

        var isThroughputTest = typeof(IThroughputTest).IsAssignableFrom(perfTestType);
        if (isThroughputTest)
        {
            if (TryCreateTest<IThroughputTest>(perfTestType, options, out var test) && ValidateTest(test.RequiredProcessorCount, options))
            {
                using var session = new ThroughputTestSession(test, options, outputDirectoryPath);
                session.Execute();
            }
            return;
        }

        var isLatencyTest = typeof(ILatencyTest).IsAssignableFrom(perfTestType);
        if (isLatencyTest)
        {
            if (TryCreateTest<ILatencyTest>(perfTestType, options, out var test) && ValidateTest(test.RequiredProcessorCount, options))
            {
                var session = new LatencyTestSession(test, options, outputDirectoryPath);
                session.Execute();
            }
            return;
        }

        throw new NotSupportedException($"Invalid test type: {perfTestType.Name}");
    }

    private static bool TryCreateTest<T>(Type testType, ProgramOptions options, out T test)
    {
        if (testType.GetConstructor([typeof(ProgramOptions)]) is { } constructor)
        {
            test = (T)constructor.Invoke([options]);
            return true;
        }

        test = (T)Activator.CreateInstance(testType);
        return test != null;
    }

    private static bool ValidateTest(int requiredProcessorCount, ProgramOptions options)
    {
        var availableProcessors = Environment.ProcessorCount;
        if (requiredProcessorCount > availableProcessors)
        {
            Console.Error.WriteLine("Error: your system has insufficient CPUs to execute the test efficiently.");
            Console.Error.WriteLine($"CPU count required = {requiredProcessorCount}, available = {availableProcessors}");
            return false;
        }

        if (requiredProcessorCount > options.CpuSet.Length)
        {
            Console.Error.WriteLine("Error: the CPU set is two small to execute the test efficiently.");
            Console.Error.WriteLine($"CPU count required = {requiredProcessorCount}, CPU set length = {options.CpuSet.Length}");
            return false;
        }

        return true;
    }
}
