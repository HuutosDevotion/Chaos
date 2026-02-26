using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the in-app modal overlay system on MainViewModel.
/// Covers state transitions for the Create Channel, Rename Channel, and
/// Delete Channel modals — open, close, pre-population, and CanExecute
/// guards — without any server or UI thread dependency.
/// </summary>
public class ModalStateTests
{
    private static ChannelViewModel TextChannel(string name = "general") =>
        new(new ChannelDto { Id = 1, Name = name, Type = ChannelType.Text });

    // ── initial defaults ──────────────────────────────────────────────────────

    [Fact]
    public void InitialState_AllModalsAreClosed()
    {
        var vm = new MainViewModel();

        Assert.False(vm.IsCreateChannelModalOpen);
        Assert.False(vm.IsRenameChannelModalOpen);
        Assert.False(vm.IsDeleteChannelModalOpen);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void InitialState_ModalChannelNameIsEmpty()
    {
        var vm = new MainViewModel();

        Assert.Equal(string.Empty, vm.ModalChannelName);
    }

    [Fact]
    public void InitialState_ModalIsVoiceTypeIsFalse()
    {
        var vm = new MainViewModel();

        Assert.False(vm.ModalIsVoiceType);
    }

    // ── open create channel modal ─────────────────────────────────────────────

    [Fact]
    public void CreateChannelCommand_OpensCreateModal()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        Assert.True(vm.IsCreateChannelModalOpen);
    }

    [Fact]
    public void CreateChannelCommand_SetsIsAnyModalOpen()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        Assert.True(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CreateChannelCommand_DoesNotOpenOtherModals()
    {
        var vm = new MainViewModel();

        vm.CreateChannelCommand.Execute(null);

        Assert.False(vm.IsRenameChannelModalOpen);
        Assert.False(vm.IsDeleteChannelModalOpen);
    }

    [Fact]
    public void CreateChannelCommand_ClearsModalChannelName()
    {
        var vm = new MainViewModel();
        vm.ModalChannelName = "leftover";

        vm.CreateChannelCommand.Execute(null);

        Assert.Equal(string.Empty, vm.ModalChannelName);
    }

    [Fact]
    public void CreateChannelCommand_DefaultsToTextType()
    {
        var vm = new MainViewModel();
        vm.ModalIsVoiceType = true; // simulate leftover state

        vm.CreateChannelCommand.Execute(null);

        Assert.False(vm.ModalIsVoiceType);
    }

    [Fact]
    public void CreateChannelCommand_RaisesPropertyChangedForCreateModalAndAnyModal()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CreateChannelCommand.Execute(null);

        Assert.Contains(nameof(MainViewModel.IsCreateChannelModalOpen), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── open rename channel modal ─────────────────────────────────────────────

    [Fact]
    public void RenameChannelCommand_OpensRenameModal()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(TextChannel());

        Assert.True(vm.IsRenameChannelModalOpen);
    }

    [Fact]
    public void RenameChannelCommand_PrePopulatesChannelName()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(TextChannel("announcements"));

        Assert.Equal("announcements", vm.ModalChannelName);
    }

    [Fact]
    public void RenameChannelCommand_DoesNotOpenOtherModals()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(TextChannel());

        Assert.False(vm.IsCreateChannelModalOpen);
        Assert.False(vm.IsDeleteChannelModalOpen);
    }

    [Fact]
    public void RenameChannelCommand_WithNullParameter_DoesNotOpenModal()
    {
        var vm = new MainViewModel();

        vm.RenameChannelCommand.Execute(null);

        Assert.False(vm.IsRenameChannelModalOpen);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void RenameChannelCommand_RaisesPropertyChangedForRenameModalAndAnyModal()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.RenameChannelCommand.Execute(TextChannel());

        Assert.Contains(nameof(MainViewModel.IsRenameChannelModalOpen), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── open delete channel modal ─────────────────────────────────────────────

    [Fact]
    public void DeleteChannelCommand_OpensDeleteModal()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(TextChannel());

        Assert.True(vm.IsDeleteChannelModalOpen);
    }

    [Fact]
    public void DeleteChannelCommand_DeleteModalMessageContainsChannelName()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(TextChannel("important-channel"));

        Assert.Contains("important-channel", vm.DeleteModalMessage);
    }

    [Fact]
    public void DeleteChannelCommand_DoesNotOpenOtherModals()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(TextChannel());

        Assert.False(vm.IsCreateChannelModalOpen);
        Assert.False(vm.IsRenameChannelModalOpen);
    }

    [Fact]
    public void DeleteChannelCommand_WithNullParameter_DoesNotOpenModal()
    {
        var vm = new MainViewModel();

        vm.DeleteChannelCommand.Execute(null);

        Assert.False(vm.IsDeleteChannelModalOpen);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void DeleteChannelCommand_RaisesPropertyChangedForDeleteModalAndAnyModal()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.DeleteChannelCommand.Execute(TextChannel());

        Assert.Contains(nameof(MainViewModel.IsDeleteChannelModalOpen), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── close modal ───────────────────────────────────────────────────────────

    [Fact]
    public void CloseModal_AfterCreate_ClosesModal()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);

        vm.CloseModalCommand.Execute(null);

        Assert.False(vm.IsCreateChannelModalOpen);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_AfterRename_ClosesModalAndClearsName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("old-name"));

        vm.CloseModalCommand.Execute(null);

        Assert.False(vm.IsRenameChannelModalOpen);
        Assert.Equal(string.Empty, vm.ModalChannelName);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_AfterDelete_ClosesModal()
    {
        var vm = new MainViewModel();
        vm.DeleteChannelCommand.Execute(TextChannel("to-delete"));

        vm.CloseModalCommand.Execute(null);

        Assert.False(vm.IsDeleteChannelModalOpen);
        Assert.False(vm.IsAnyModalOpen);
    }

    [Fact]
    public void CloseModal_ResetsModalIsVoiceType()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        vm.ModalIsVoiceType = true;

        vm.CloseModalCommand.Execute(null);

        Assert.False(vm.ModalIsVoiceType);
    }

    [Fact]
    public void CloseModal_RaisesPropertyChangedForClosedModal()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CloseModalCommand.Execute(null);

        Assert.Contains(nameof(MainViewModel.IsCreateChannelModalOpen), raised);
        Assert.Contains(nameof(MainViewModel.IsAnyModalOpen), raised);
    }

    // ── ConfirmCreate CanExecute ──────────────────────────────────────────────

    [Fact]
    public void ConfirmCreate_CannotExecuteWithEmptyName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        vm.ModalChannelName = string.Empty;

        Assert.False(vm.ConfirmCreateChannelCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmCreate_CannotExecuteWithWhitespaceName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        vm.ModalChannelName = "   ";

        Assert.False(vm.ConfirmCreateChannelCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmCreate_CanExecuteWithValidName()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);
        vm.ModalChannelName = "new-channel";

        Assert.True(vm.ConfirmCreateChannelCommand.CanExecute(null));
    }

    // ── ConfirmRename CanExecute ──────────────────────────────────────────────

    [Fact]
    public void ConfirmRename_CannotExecuteWithEmptyName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("general"));
        vm.ModalChannelName = string.Empty;

        Assert.False(vm.ConfirmRenameChannelCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmRename_CannotExecuteWithWhitespaceName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("general"));
        vm.ModalChannelName = "  ";

        Assert.False(vm.ConfirmRenameChannelCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmRename_CanExecuteWithValidName()
    {
        var vm = new MainViewModel();
        vm.RenameChannelCommand.Execute(TextChannel("general"));
        vm.ModalChannelName = "renamed";

        Assert.True(vm.ConfirmRenameChannelCommand.CanExecute(null));
    }

    // ── ModalIsVoiceType property ─────────────────────────────────────────────

    [Fact]
    public void ModalIsVoiceType_CanBeSetToTrue()
    {
        var vm = new MainViewModel();
        vm.CreateChannelCommand.Execute(null);

        vm.ModalIsVoiceType = true;

        Assert.True(vm.ModalIsVoiceType);
    }

    [Fact]
    public void ModalIsVoiceType_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ModalIsVoiceType = true;

        Assert.Contains(nameof(MainViewModel.ModalIsVoiceType), raised);
    }
}
