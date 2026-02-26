using System;
using System.Threading.Tasks;

namespace AvaloniaAppMPV.Services;

/// <summary>
/// Abstracts file picker dialogs away from the ViewModel.
/// </summary>
public interface IDialogService
{
    Task<string?> OpenVideoFileAsync();
}
