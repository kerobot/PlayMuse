using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

public interface ISettingsService
{
    AppSettings Load();

    void Save(AppSettings settings);
}
