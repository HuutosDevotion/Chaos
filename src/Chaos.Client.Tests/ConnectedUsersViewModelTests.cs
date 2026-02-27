using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the ConnectedUsers collection and ConnectedUsersHeader property
/// on MainViewModel, which drive the right sidebar user list.
/// </summary>
public class ConnectedUsersViewModelTests
{
    // ── initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void ConnectedUsers_InitialState_IsEmpty()
    {
        var vm = new MainViewModel();

        Assert.Empty(vm.ConnectedUsers);
    }

    [Fact]
    public void ConnectedUsersHeader_EmptyList_ShowsZeroCount()
    {
        var vm = new MainViewModel();

        Assert.Equal("MEMBERS — 0", vm.ConnectedUsersHeader);
    }

    // ── header reflects count ─────────────────────────────────────────────────

    [Fact]
    public void ConnectedUsersHeader_OneUser_ShowsCountOfOne()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");

        Assert.Equal("MEMBERS — 1", vm.ConnectedUsersHeader);
    }

    [Fact]
    public void ConnectedUsersHeader_MultipleUsers_ShowsCorrectCount()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");
        vm.ConnectedUsers.Add("Bob");
        vm.ConnectedUsers.Add("Charlie");

        Assert.Equal("MEMBERS — 3", vm.ConnectedUsersHeader);
    }

    [Fact]
    public void ConnectedUsersHeader_AfterRemovingUser_DecreasesCount()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");
        vm.ConnectedUsers.Add("Bob");

        vm.ConnectedUsers.Remove("Alice");

        Assert.Equal("MEMBERS — 1", vm.ConnectedUsersHeader);
    }

    [Fact]
    public void ConnectedUsersHeader_AfterClear_ShowsZeroCount()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");
        vm.ConnectedUsers.Add("Bob");

        vm.ConnectedUsers.Clear();

        Assert.Equal("MEMBERS — 0", vm.ConnectedUsersHeader);
    }

    // ── collection membership ─────────────────────────────────────────────────

    [Fact]
    public void ConnectedUsers_AddUser_ContainsUser()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");

        Assert.Contains("Alice", vm.ConnectedUsers);
    }

    [Fact]
    public void ConnectedUsers_RemoveUser_NoLongerContainsUser()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");
        vm.ConnectedUsers.Add("Bob");

        vm.ConnectedUsers.Remove("Alice");

        Assert.DoesNotContain("Alice", vm.ConnectedUsers);
        Assert.Contains("Bob", vm.ConnectedUsers);
    }

    [Fact]
    public void ConnectedUsers_Clear_EmptiesCollection()
    {
        var vm = new MainViewModel();
        vm.ConnectedUsers.Add("Alice");
        vm.ConnectedUsers.Add("Bob");

        vm.ConnectedUsers.Clear();

        Assert.Empty(vm.ConnectedUsers);
    }
}
