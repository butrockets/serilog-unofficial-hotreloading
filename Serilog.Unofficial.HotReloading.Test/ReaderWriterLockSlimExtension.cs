using System.Threading;
using Xunit;

namespace Serilog.Unofficial.HotReloading.Test;

public class ReaderWriterLockSlimExtension
{
    [Fact]
    public void AutoReadLocker()
    {
        using var l = new ReaderWriterLockSlim();
        Assert.Equal(0, l.CurrentReadCount);
        using(l.ReadLock())
        {
            Assert.Equal(1, l.CurrentReadCount);
        }
        Assert.Equal(0, l.CurrentReadCount);
    }

    [Fact]
    public void AutoWriteLocker()
    {
        using var l = new ReaderWriterLockSlim();
        Assert.False(l.IsWriteLockHeld);
        using (l.WriteLock())
        {
            Assert.True(l.IsWriteLockHeld);
        }
        Assert.False(l.IsWriteLockHeld);
    }
}