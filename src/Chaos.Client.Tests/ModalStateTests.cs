using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the in-app modal overlay system.
/// MainViewModel exposes a single ActiveModal property whose runtime type
/// determines which DataTemplate WPF renders. Tests verify that the right
/// modal ViewModel is created with the right initial state, that closing
/// works, and that each modal's Confirm/Cancel CanExecute guards are correct.
/// </summary>
public class ModalStateTests
{
    private static ChannelViewModel TextChannel(string name = "general") =>
        new(new ChannelDto { Id = 1, Name = name, Type = ChannelType.Text });

    // ── initial defaults ──────────────────────────────────────────────────────

    [Fact]
    public void InitialState_ActiveModalIsNull()
    {
        var vm = new MainViewModel();

        Assert.Null(vm.ActiveModal);
    }

    [Fact]
    public void InitialState_IsAnyModalOpenIsFalse()
    {
        var vm = new MainViewModel();

        Assert.False(vm.IsAnyModalOpen);
    }

    // ── open create channel modal ─────────────────────────────────────────────

    [Fact]
    public void CreateChannelCommand_SetsActiveModalToCreateChannelModalViewModel()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
    }

    [Fact]
    public void CreateChannelCommand_SetsIsAnyModalOpen()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        Assert.True(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CreateChannelCommand_ModalStartsWithEmptyName()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        Assert.Equal(string.Empty, modal.ChannelName);
    }

    [Fact]
    public void CreateChannelCommand_ModalDefaultsToTextType()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        Assert.False(modal.IsVoiceType);
    }

    [Fact]
    public void CreateChannelCommand_RaisesPropertyChangedForActiveModalAndIsAnyModalOpen()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CreateChannelCommand.Execute(null);

        Assert.Contains(nameof(MainViewModel.ActiveModal), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── open rename channel modal ─────────────────────────────────────────────

    [Fact]
    public void RenameChannelCommand_SetsActiveModalToRenameChannelModalViewModel()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(TextChannel());

        Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);
    }

    [Fact]
    public void RenameChannelCommand_PrePopulatesChannelName()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(TextChannel("announcements"));

        var modal = Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);
        Assert.Equal("announcements", modal.ChannelName);
    }

    [Fact]
    public void RenameChannelCommand_WithNullParameter_DoesNotOpenModal()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(null);

        Assert.Null(vm.ActiveModal);
        Assert.False(vm.IsAnyModalOpen);
    }

    // ── open delete channel modal ─────────────────────────────────────────────

    [Fact]
    public void DeleteChannelCommand_SetsActiveModalToDeleteChannelModalViewModel()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(TextChannel());

        Assert.IsType<DeleteChannelModalViewModel>(vm.ActiveModal);
    }

    [Fact]
    public void DeleteChannelCommand_ModalMessageContainsChannelName()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(TextChannel("important-channel"));

        var modal = Assert.IsType<DeleteChannelModalViewModel>(vm.ActiveModal);
        Assert.Contains("important-channel", modal.Message);
    }

    [Fact]
    public void DeleteChannelCommand_WithNullParameter_DoesNotOpenModal()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(null);

        Assert.Null(vm.ActiveModal);
        Assert.False(vm.IsAnyModalOpen);
    }

    // ── close modal ───────────────────────────────────────────────────────────

    [Fact]
    public void CloseModal_AfterCreate_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);

        vm.CloseModal();

        Assert.Null(vm.ActiveModal);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_AfterRename_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel());

        vm.CloseModal();

        Assert.Null(vm.ActiveModal);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_AfterDelete_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.DeleteChannelCommand.Execute(TextChannel());

        vm.CloseModal();

        Assert.Null(vm.ActiveModal);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_RaisesPropertyChangedForActiveModalAndIsAnyModalOpen()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CloseModal();

        Assert.Contains(nameof(MainViewModel.ActiveModal), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── modal Cancel command closes the modal ─────────────────────────────────

    [Fact]
    public void CreateModal_CancelCommand_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);

        modal.Cancel.Execute(null);

        Assert.Null(vm.ActiveModal);
    }

    [Fact]
    public void RenameModal_CancelCommand_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel());
        var modal = Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);

        modal.Cancel.Execute(null);

        Assert.Null(vm.ActiveModal);
    }

    [Fact]
    public void DeleteModal_CancelCommand_SetsActiveModalNull()
    {
        var vm = new MainViewModel();
        vm.DeleteChannelCommand.Execute(TextChannel());
        var modal = Assert.IsType<DeleteChannelModalViewModel>(vm.ActiveModal);

        modal.Cancel.Execute(null);

        Assert.Null(vm.ActiveModal);
    }

    // ── Confirm CanExecute guards ─────────────────────────────────────────────

    [Fact]
    public void CreateModal_Confirm_CannotExecuteWithEmptyName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        modal.ChannelName = string.Empty;

        Assert.False(modal.Confirm.CanExecute(null));
    }

    [Fact]
    public void CreateModal_Confirm_CannotExecuteWithWhitespaceName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        modal.ChannelName = "   ";

        Assert.False(modal.Confirm.CanExecute(null));
    }

    [Fact]
    public void CreateModal_Confirm_CanExecuteWithValidName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        modal.ChannelName = "my-channel";

        Assert.True(modal.Confirm.CanExecute(null));
    }

    [Fact]
    public void RenameModal_Confirm_CannotExecuteWithEmptyName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("general"));
        var modal = Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);
        modal.ChannelName = string.Empty;

        Assert.False(modal.Confirm.CanExecute(null));
    }

    [Fact]
    public void RenameModal_Confirm_CanExecuteWithValidName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("general"));
        var modal = Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);
        modal.ChannelName = "renamed";

        Assert.True(modal.Confirm.CanExecute(null));
    }

    // ── modal ViewModel INotifyPropertyChanged ────────────────────────────────

    [Fact]
    public void CreateModal_ChannelName_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        var raised = new List<string?>();
        modal.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        modal.ChannelName = "test";

        Assert.Contains(nameof(CreateChannelModalViewModel.ChannelName), raised);
    }

    [Fact]
    public void CreateModal_IsVoiceType_CanBeSetAndRaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var modal = Assert.IsType<CreateChannelModalViewModel>(vm.ActiveModal);
        var raised = new List<string?>();
        modal.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        modal.IsVoiceType = true;

        Assert.True(modal.IsVoiceType);
        Assert.Contains(nameof(CreateChannelModalViewModel.IsVoiceType), raised);
    }

    [Fact]
    public void RenameModal_ChannelName_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("old"));
        var modal = Assert.IsType<RenameChannelModalViewModel>(vm.ActiveModal);
        var raised = new List<string?>();
        modal.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        modal.ChannelName = "new-name";

        Assert.Contains(nameof(RenameChannelModalViewModel.ChannelName), raised);
    }
}
