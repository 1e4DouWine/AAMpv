using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaAppMPV.UI.Common;

/// <summary>
/// 所有 ViewModel 的公共基类。
/// 目前主要统一继承 ObservableObject，后续如果要挂公共状态也有落点。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
