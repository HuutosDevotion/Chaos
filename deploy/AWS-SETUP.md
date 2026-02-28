# Chaos Server — Deployment Guide

Complete guide to deploy Chaos Server to AWS EC2 with GitHub Actions CI/CD.

**Do these in order:** AWS first (you need the EC2 IP and key), then GitHub Actions.

---

## Part 1: AWS EC2 Setup

### 1. Launch EC2 Instance

1. Go to **EC2 → Launch Instance** in the AWS Console
2. Configure:
   - **Name:** `chaos-server`
   - **AMI:** Ubuntu 24.04 LTS (HVM, SSD)
   - **Instance type:** `t3.small` (~$0.02/hr, ~$15/mo)
   - **Key pair:** Create new → download `.pem` file (you'll need this for GitHub secrets)
   - **Storage:** 20 GB gp3 (default is fine)

### 2. Configure Security Group

Create or edit the security group with these inbound rules:

| Port | Protocol | Source | Purpose |
|------|----------|--------|---------|
| 22 | TCP | 0.0.0.0/0 | SSH access (GitHub Actions + you) |
| 5000 | TCP | 0.0.0.0/0 | SignalR / HTTP |
| 9000 | UDP | 0.0.0.0/0 | Voice relay |

> SSH is open to `0.0.0.0/0` because GitHub Actions deploys over SSH and GitHub's runner IPs change frequently. The `.pem` key is the real access control here. If you want tighter security, you can use [GitHub's published IP ranges](https://api.github.com/meta) but they change over time.

### 3. Connect and Run Setup

```bash
# SSH into the instance
ssh -i your-key.pem ubuntu@<EC2_PUBLIC_IP>

# Clone the repo (or just copy the deploy folder)
git clone https://github.com/YOUR_ORG/chaos.git
cd chaos

# Run the setup script
sudo bash deploy/setup.sh
```

### 4. Note Your EC2 Details

You'll need these three things for GitHub Actions (next section):

| Value | Where to Find It |
|-------|-----------------|
| **Public IP** | EC2 Console → Instances → your instance → "Public IPv4 address" |
| **SSH key** | The `.pem` file you downloaded in step 1 |
| **Username** | `ubuntu` (default for Ubuntu AMIs) |

---

## Part 2: GitHub Actions Setup

### How It Works

The workflow file (`.github/workflows/ci-deploy.yml`) is already in the repo. GitHub Actions automatically detects it — no manual "enable" step needed. It triggers on:
- **Push to `main`** — runs tests, then deploys to EC2
- **Pull requests targeting `main`** — runs tests only (no deploy)

### Step 1: Verify Actions Are Enabled

1. Go to your GitHub repo in a browser
2. Click the **Actions** tab at the top
3. If you see a prompt to enable workflows, click **"I understand my workflows, go ahead and enable them"**
4. If you already see the workflow listed, you're good

> If the Actions tab is missing entirely, go to **Settings → Actions → General** and select **"Allow all actions and reusable workflows"**

### Step 2: Add Repository Secrets

The deploy job needs SSH access to your EC2 instance. These secrets are referenced in the workflow as `${{ secrets.SECRET_NAME }}`.

1. Go to your repo on GitHub
2. Click **Settings** (gear icon, far right in the tab bar)
3. In the left sidebar: **Secrets and variables → Actions**
4. Click **"New repository secret"** for each:

| Secret Name | What to Put | Example |
|-------------|-------------|---------|
| `EC2_HOST` | Your EC2 instance's public IP (from Part 1, step 4) | `54.123.45.67` |
| `EC2_SSH_KEY` | The **entire contents** of your `.pem` key file | Open the `.pem` in a text editor, select all, paste |
| `EC2_USER` | The SSH username for your EC2 | `ubuntu` |

> **Important:** For `EC2_SSH_KEY`, paste the full key including the `-----BEGIN` and `-----END` lines. Make sure there are no trailing spaces or extra newlines.

### Step 3: Push to Deploy

Push a commit to `main` (or merge a PR). The GitHub Action will:
1. Build and test on Windows
2. Publish `Chaos.Server` for `linux-x64`
3. SCP the files to `/opt/chaos/` on your EC2
4. Restart the `chaos` systemd service

### Step 4: Monitor the Workflow

1. Go to the **Actions** tab
2. Click on the latest run
3. You'll see two jobs:
   - **test** (Windows) — builds solution + runs all tests
   - **deploy** (Ubuntu) — publishes server + deploys to EC2 (only on push to main)
4. Click a job to expand step-by-step logs
5. Green checkmark = success, red X = failure (click to see error details)

---

## Verify Everything Works

```bash
# From your local machine
curl http://<EC2_PUBLIC_IP>:5000/
# Should return: Chaos Server is running

# On the EC2 instance
sudo systemctl status chaos
sudo journalctl -u chaos -f
```

Then point your Chaos.Client at `<EC2_PUBLIC_IP>:5000` and connect.

---

## Reference

### Useful Commands

```bash
# Restart the server
sudo systemctl restart chaos

# View live logs
sudo journalctl -u chaos -f

# Check if ports are open
sudo ss -tlnp | grep 5000
sudo ss -ulnp | grep 9000
```

### Troubleshooting

| Problem | Fix |
|---------|-----|
| Workflow doesn't appear in Actions tab | Make sure `.github/workflows/ci-deploy.yml` is committed and pushed to `main` |
| Tests fail | Click the failed **test** job → expand **Run tests** to see which tests failed |
| Deploy fails with "Permission denied" | Check that `EC2_SSH_KEY` secret contains the correct private key with no extra whitespace |
| Deploy fails with "Connection refused" | Check that EC2 is running and security group allows SSH (port 22) |
| "Resource not accessible by integration" | **Settings → Actions → General → Workflow permissions** → select **"Read and write permissions"** |
| Server not responding after deploy | SSH in and check `sudo journalctl -u chaos -f` for errors |

### Cost Estimate

- **t3.small** (2 vCPU, 2 GB RAM): ~$15/month running 24/7
- **t3.micro** (2 vCPU, 1 GB RAM): ~$8/month (may work for small groups)
- Stop the instance when not in use to save costs

### Elastic IP (Optional)

By default, the public IP changes when you stop/start the instance. To get a static IP:

1. Go to **EC2 → Elastic IPs → Allocate**
2. Associate it with your instance
3. Update `EC2_HOST` GitHub secret with the new IP
