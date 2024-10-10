// Copyright 2024 Giuseppe Marazzi
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Threading;

namespace Serilog.Unofficial.HotReloading;

static class ReaderWriterLockSlimExtension
{
    public static AutoReadLocker ReadLock(this ReaderWriterLockSlim readerWriterLock)
        => new AutoReadLocker(readerWriterLock);

    public static AutoWriteLocker WriteLock(this ReaderWriterLockSlim readerWriterLock)
        => new AutoWriteLocker(readerWriterLock);

    //
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-8.0/using#pattern-based-using
    public ref struct AutoReadLocker
    {
        private readonly ReaderWriterLockSlim _readerWriterLock;
        public AutoReadLocker(ReaderWriterLockSlim readerWriterLock)
        {
            readerWriterLock.EnterReadLock();
            _readerWriterLock = readerWriterLock;
        }
        public void Dispose() => _readerWriterLock.ExitReadLock();
    }

    public ref struct AutoWriteLocker
    {
        private readonly ReaderWriterLockSlim _readerWriterLock;
        public AutoWriteLocker(ReaderWriterLockSlim readerWriterLock)
        {
            readerWriterLock.EnterWriteLock();
            _readerWriterLock = readerWriterLock;
        }
        public void Dispose() => _readerWriterLock.ExitWriteLock();
    }
}
