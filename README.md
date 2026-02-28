# Chaos

A self-hosted Discord-style voice and text chat app built with .NET 8, WPF, SignalR, and UDP voice relay.

## Setup

### Prerequisites

1. Clone the repo
   ```bash
   git clone git@github.com:HuutosDevotion/Chaos.git
   ```
   > If you get a permission error, you may need to [set up an SSH key](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/generating-a-new-ssh-key-and-adding-it-to-the-ssh-agent) for GitHub.

2. Download and run the [Visual Studio Installer](https://visualstudio.microsoft.com/downloads/)

3. Install **Visual Studio 2022** with these workloads:
   - **.NET Multi-platform App UI development**
   - **.NET desktop development**

4. Install [Claude Code](https://claude.ai/code) in your preferred environment (terminal, app, or browser)

### First Run

1. Open the solution in Visual Studio
2. **Right-click the solution → Build Solution** — it should build with no errors
3. **Right-click the solution → Properties → Common → Configure Startup Projects**
   - Select **Multiple startup projects**
   - Make sure **Server** is listed before **Client** (use the arrows if needed)
   - Set the action to **Start** for both
4. Press **F5** to launch — you're ready to develop

---

## Feature Development Workflow

1. **Create a branch** for your feature
   ```bash
   git checkout -b yourname/feature-name
   ```

2. **Develop using Claude Code**

3. **Commit checkpoints** as you go
   ```bash
   git add .
   git commit -m "short description of what you did"
   ```

4. **Add regression tests** — ask Claude to write tests covering your feature

5. **Run all tests** in Visual Studio: `Tests → Run All Tests`

6. **Push your branch** once all tests pass
   ```bash
   git push
   ```
   > The first push on a new branch will prompt you to set the upstream — follow the command the terminal suggests.

---

## Merging to Main

1. Open GitHub — you should see a banner for your recent branch with a **"Compare & pull request"** button
2. Set a clear title and include testing instructions in the description
3. Request a review from another developer
4. Once approved, merge using **Squash and Merge**

### Resolving Merge Conflicts

If you hit merge conflicts, ask Viraaj for help. If you're comfortable with rebasing:

```bash
git pull
git rebase origin/main
# resolve conflicts...
git push --force-with-lease
```

---

## Claude Code Tips

- **Give file paths upfront.** If you know where your code should go, tell Claude — it saves context and avoids changes to unrelated files.
- **Be specific.** Vague instructions lead to broad changes, which means harder merges and a higher chance of breaking things.
- **Resume sessions.** When you exit Claude, it prints a command hash — use it to resume right where you left off.
- **Keep features small.** Smaller PRs are easier to review and merge cleanly. For large infrastructure changes, use **Opus 4.6** and coordinate with the team before starting.
- **Add more as you see fit.** test add for ci/cd
