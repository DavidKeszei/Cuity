using Kinesis.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kinesis.Native;

/// <summary>
/// Represents a thin wrapper around a <see cref="IConsoleSource{T}"/>.
/// </summary>
/// <typeparam name="TReader">Reader/Source of the console info.</typeparam>
/// <typeparam name="TData">Information, which can be read out from the <typeparamref name="TReader"/>.</typeparam>
internal readonly struct ConsoleReader<TReader, TData>(TReader reader): IConsoleSource<TData> where TReader: class, IConsoleSource<TData> {
    private readonly TReader m_reader = reader;

    public bool Read(out TData? result) => m_reader.Read(out result);
}
