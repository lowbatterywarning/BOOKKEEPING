using System.Collections.ObjectModel;
using Bookkeeping.Core.Models;
namespace Bookkeeping.Wpf.ViewModels;
internal static class PickerRefresh
{
    public static void Update(ObservableCollection<OrgProgram> items, List<OrgProgram> fresh) =>
        Update(items, fresh, p => p.Id, (old, current) => { old.Name = current.Name; old.IsActive = current.IsActive; });
    public static void Update(ObservableCollection<Sponsor> items, List<Sponsor> fresh) =>
        Update(items, fresh, s => s.Id, (old, current) => { old.Name = current.Name; old.IsActive = current.IsActive; });
    private static void Update<T>(ObservableCollection<T> items, List<T> fresh, Func<T,int> id, Action<T,T> copy)
    {
        var wanted = fresh.Select(id).ToHashSet();
        foreach (var item in items.Where(i => !wanted.Contains(id(i))).ToList()) items.Remove(item);
        for (var index = 0; index < fresh.Count; index++)
        {
            var current = fresh[index];
            var old = items.FirstOrDefault(i => id(i) == id(current));
            if (old is null) items.Insert(index, current);
            else { copy(old, current); var previous = items.IndexOf(old); if (previous != index) items.Move(previous, index); }
        }
    }
}
