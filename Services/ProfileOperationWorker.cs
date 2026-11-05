using Microsoft.EntityFrameworkCore;
using Multilider.Operations;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Models;
namespace MSMultilider.Comercial.Services;
public sealed class ProfileOperationWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<ProfileOperationWorker> logger):BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stop) {
        if(!config.GetValue<bool>("Operations:WorkerEnabled"))return;
        while(!stop.IsCancellationRequested) {
            try {
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var before=DateTime.UtcNow.AddSeconds(-5);
                var now=DateTime.UtcNow;
                var ids=await db.Set<ProfileOperation>().AsNoTracking().Where(x=>(x.State=="Pendiente"||x.State=="Vinculada")&&x.ErrorCode!="CREDENTIAL_REQUIRED"&&x.ErrorCode!="IDENTITY_CONFLICT"&&x.ErrorCode!="REAUTHORIZATION_REQUIRED"&&x.NextAttempt<=now&&x.UpdatedAt<before)
                    .OrderBy(x=>x.UpdatedAt).Select(x=>x.Id).Take(10).ToListAsync(stop);
                foreach(var id in ids) {
                    db.ChangeTracker.Clear();var op=(await db.Set<ProfileOperation>().FindAsync([id],stop))!;
                    try{await scope.ServiceProvider.GetRequiredService<ProfileCoordinator>().Continue(op,null,stop);}
                    catch(Exception e)when(e is ProtocolException or HttpRequestException or TaskCanceledException){await scope.ServiceProvider.GetRequiredService<ProfileCoordinator>().RecordFailure(id,e,stop);}
                }
            } catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Profile recovery paused: {FailureType}",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(5),stop);
        }
    }
}
