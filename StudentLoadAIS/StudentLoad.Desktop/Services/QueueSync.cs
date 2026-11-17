using System.Net;
using StudentLoad.Desktop.Data;

namespace StudentLoad.Desktop.Services;

internal sealed record SyncResult(int Sent, bool ConnectionLost, string? Blocked);

internal sealed class QueueSync(StudentLoadApi api, OfflineStore store)
{
    public async Task<SyncResult> ReplayAsync(Session session)
    {
        var sent = 0;
        foreach (var item in await store.ListAsync(session.Username))
        {
            if (item.Error is { } error)
                return new SyncResult(sent, false, error);

            try
            {
                await api.SendMutationAsync(session, item);
            }
            catch (ApiUnavailableException)
            {
                return new SyncResult(sent, true, null);
            }
            catch (ApiRejectedException ex) when (ex.Status != HttpStatusCode.Unauthorized)
            {
                await store.SetErrorAsync(session.Username, item.Sequence, ex.Message);
                return new SyncResult(sent, false, ex.Message);
            }

            await store.RemoveAsync(session.Username, item.Sequence);
            sent++;
        }
        return new SyncResult(sent, false, null);
    }
}
