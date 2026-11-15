using BiliPlay.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    [ObservableProperty] public partial bool PreferHiRes { get; set; } = true;
    partial void OnPreferHiResChanged(bool value)
    {
        Player.PreferHiRes = value;
        Store.SetSetting("preferHiRes", value ? "true" : "false");
    }
    public void SelectPlaybackQuality(string quality)
    {
        PreferHiRes = AudioQualityPreference.IsHiRes(quality);
        Player.SelectQuality(quality);
    }
}
