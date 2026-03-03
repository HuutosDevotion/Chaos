using System.Windows.Input;

namespace Chaos.Client.ViewModels;

public class DeleteChannelModalViewModel : SubmenuViewModel
{
    private readonly Func<Task> _confirm;

    public string Message { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public DeleteChannelModalViewModel(string channelName, Func<Task> confirm)
    {
        Message = $"Delete \"{channelName}\"? This cannot be undone.";
        _confirm = confirm;
        Confirm = new RelayCommand(async _ => { Close(); await _confirm(); });
        Cancel = new RelayCommand(_ => Close());
    }
}
