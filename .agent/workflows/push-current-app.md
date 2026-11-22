---
description: Build verification, safe staging, interactive commit/push following AGENT.md, and detached application launch
slash_command: /push-current-app
---

# Push Current Application

Execute the following steps in sequence:

### 1. Workspace & Rule Discovery
- Check for `AGENT.md` or `AGENTS.md` in the current workspace root.
- Read and extract project-specific configurations and guardrails:
  - Application name and running process patterns to check/terminate before builds (e.g., `*PdfToTiles*`, `*pdf-to-Tiles*`).
  - Target framework and build toolchain (e.g., `dotnet build -c Debug`, `npm run build`, etc.).
  - Output binary patterns (e.g., `bin\Debug\*\*.exe`).
  - Version numbering and commit message formatting rules (e.g., maintaining `<Version>` in `.csproj` and prefixing commit messages with `v<Version> - `).
  - Any project-specific testing, packaging, or operational isolation requirements.

### 2. Pre-Build File-Lock Check
- Identify any running processes of the compiled application using patterns discovered from `AGENT.md` or the project/solution name.
- Stop running instances cleanly to prevent binary/database file locks (`MSB3027` / `MSB3021` / SQLite WAL locks):
  ```powershell
  Get-Process | Where-Object { $_.ProcessName -like "*<AppName>*" } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  ```

### 3. Clean Build & Verification
- Execute the project's build command (e.g., `dotnet build -c Debug` for .NET solutions).
- Confirm clean compilation with exit code `0` and 0 errors.
- **Fail Fast:** If the build fails, abort the workflow immediately. Do not stage, commit, or push broken code. Present the exact compiler errors to the user.

### 4. Inspect `git status` & Safe Staging (No Blind `git add -A`)
- Run `git status --short` and `git diff --name-only`.
- Verify that every modified or untracked file directly belongs to the authorized task.
- Check that no temporary logs, build artifacts, or accidental edits are present.
- Stage **only** the approved, task-relevant files explicitly (`git add <file1> <file2> ...`). Never run blind `git add -A` when unrelated or untracked files are present.
- If unexpected modified or untracked files appear, stop and present them for user confirmation before staging anything.

### 5. Interactive Git Confirmation ("Grill Me" Policy) & Push
- Follow the interactive Git confirmation protocol:
  1. **Diff Review:** Present a concise summary of the exact files staged for commit.
  2. **Commit Suggestion:** Suggest a commit message adhering to the project's convention (e.g., `v<Version> - <Summary>` as dictated by `AGENT.md` or Conventional Commits).
  3. **Interrogation:** Ask the user:
     *"Would you like to use this suggested commit message, edit it, or provide a completely different one?"*
  4. **Final Confirmation:** Once confirmed, ask:
     *"Are you ready to commit and push these exact changes to remote now?"*
  5. **Execute Commit & Push:** Upon receiving explicit approval, commit and push to the active branch:
     ```powershell
     git commit -m "<Approved Message>"
     git push origin <CurrentBranch>
     ```
  6. **Commit Hash Logging:** Capture and prominently display the resulting SHA-1 commit hash.

### 6. Detached Post-Push Launch (If Applicable)
- If the current project is a desktop/GUI application with a compiled executable:
  - Dynamically locate the newly compiled `.exe` under the project's output directory (e.g., `bin\Debug\*\*.exe`, excluding auxiliary binaries such as `createdump.exe` or `*.vshost.exe`).
  - Launch the application detached without blocking the terminal:
    ```powershell
    Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName
    ```
  - Never run blocking launch commands like `dotnet run`.
- If the application is a web service, background daemon, or library, finish reporting without launching a GUI process.
