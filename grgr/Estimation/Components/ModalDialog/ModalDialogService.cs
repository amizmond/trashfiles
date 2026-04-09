using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Estimation.Components.ModalDialog;

public interface IModalDialogService
{
    Task Info(string message, string title = "Information");

    Task Error(string message, string title = "Error");

    Task Exception(Exception message, string title = "Exception has occurred");

    Task<bool> Confirm(string message, string title = "Confirmation", string okBtnText = "Ok");

    Task Warning(string message, string title = "Warning");

    Task<IDialogReference> Component<T>(string title) where T : IComponent;

    Task<IDialogReference> Component<T>(string title, IDictionary<string, object?> componentParameters) where T : IComponent;

    Task<IDialogReference> Component<T>(string title, IDictionary<string, object?> componentParameters, DialogOptions options) where T : IComponent;
}

public class ModalDialogService : IModalDialogService
{
    private readonly IDialogService _dialogService;

    private readonly DialogOptions _dialogOptions = new DialogOptions
    {
        BackdropClick = false,
    };

    public ModalDialogService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async Task Info(string message, string title = "Information")
    {
        var dialogParameters = new DialogParameters<DialogMessage>
        {
            { p => p.OkBtnText, "Ok" },
            { p => p.Title, title },
            { p => p.Message, message },
            { p => p.DialogType, MessageDialogStyle.Information },
        };

        var dialogRef = await _dialogService.ShowAsync<DialogMessage>(string.Empty, dialogParameters);

        await dialogRef.Result;
    }

    public async Task Warning(string message, string title = "Warning")
    {
        var dialogParameters = new DialogParameters<DialogMessage>
        {
            { p => p.OkBtnText, "Ok" },
            { p => p.Title, title },
            { p => p.Message, message },
            { p => p.DialogType, MessageDialogStyle.Warning },
        };

        var dialogRef = await _dialogService.ShowAsync<DialogMessage>(string.Empty, dialogParameters);
        await dialogRef.Result;
    }
    
    public async Task Error(string message, string title = "Error")
    {
        var dialogParameters = new DialogParameters<DialogMessage>
        {
            { p => p.OkBtnText, "Ok" },
            { p => p.Title, title },
            { p => p.Message, message },
            { p => p.DialogType, MessageDialogStyle.Error },
        };

        var dialogRef = await _dialogService.ShowAsync<DialogMessage>(string.Empty, dialogParameters, _dialogOptions);
        await dialogRef.Result;
    }

    public async Task<bool> Confirm(string message, string title = "Confirmation", string okBtnText = "Ok")
    {
        var dialogParameters = new DialogParameters<DialogMessage>
        {
            { p => p.OkBtnText, okBtnText},
            { p => p.CancelBtnText, "Cancel" },
            { p => p.Title, title },
            { p => p.Message, message },
            { p => p.DialogType, MessageDialogStyle.Confirmation },
        };

        var dialogRef = await _dialogService.ShowAsync<DialogMessage>(string.Empty, dialogParameters, _dialogOptions);
        var result = await dialogRef.Result;
        return result != null && !result.Canceled;
    }

    public Task<IDialogReference> Component<T>(string title)
        where T : IComponent
    {
        var dialogParameters = new DialogParameters<DialogComponent>
        {
            { p => p.Title, title },
            { p => p.ComponentType, typeof(T) },
        };

        return _dialogService.ShowAsync<DialogComponent>(string.Empty, dialogParameters, _dialogOptions);
    }

    public Task<IDialogReference> Component<T>(string title, IDictionary<string, object?> componentParameters)
        where T : IComponent
    {
        var dialogParameters = new DialogParameters<DialogComponent>
        {
            { p => p.Title, title },
            { p => p.ComponentType, typeof(T) },
            { p => p.ComponentParameters, componentParameters },
        };

        return _dialogService.ShowAsync<DialogComponent>(string.Empty, dialogParameters, _dialogOptions);
    }

    public Task<IDialogReference> Component<T>(string title, IDictionary<string, object?> componentParameters, DialogOptions options)
        where T : IComponent
    {
        var dialogParameters = new DialogParameters<DialogComponent>
        {
            { p => p.Title, title },
            { p => p.ComponentType, typeof(T) },
            { p => p.ComponentParameters, componentParameters },
        };

        var mergedOptions = new DialogOptions
        {
            BackdropClick = options.BackdropClick ?? _dialogOptions.BackdropClick,
            MaxWidth = options.MaxWidth,
            FullWidth = options.FullWidth,
        };

        return _dialogService.ShowAsync<DialogComponent>(string.Empty, dialogParameters, mergedOptions);
    }

    public async Task Exception(Exception message, string title = "Exception has occurred")
    {
        var dialogParameters = new DialogParameters<DialogException>
        {
            { p => p.Title, title },
            { p => p.Message, message.Message },
            { p => p.Source, message.Source },
            { p => p.StackTrace, message.StackTrace },
        };

        await _dialogService.ShowAsync<DialogException>(string.Empty, dialogParameters, _dialogOptions);
    }
}