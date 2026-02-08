# GitHub Setup Guide - Privacy & Security

This guide will help you safely upload your project to GitHub while protecting sensitive information.

## 🔒 What Will Be Excluded (Privacy Protection)

The `.gitignore` file has been configured to exclude:

### ✅ Sensitive Files (NEVER committed):
- `appsettings.Development.json` - Contains your local development settings
- `appsettings.Production.json` - Contains production secrets (if exists)
- `*.secrets.json` - Any secret files
- Database files (`.mdf`, `.ldf`, `.db`, `.sqlite`)
- Environment files (`.env`, `.env.local`)

### ✅ Build Artifacts (Not needed in repo):
- `bin/` - Compiled binaries
- `obj/` - Build objects
- `*.dll`, `*.exe`, `*.pdb` - Compiled files

### ✅ IDE/Editor Files:
- `.vs/` - Visual Studio files
- `.vscode/` - VS Code settings (except shared configs)
- `.idea/` - JetBrains Rider files

### ✅ User-Specific Files:
- `*.user` - User-specific settings
- `*.suo` - Solution user options
- Log files

## 📝 What WILL Be Committed (Safe to share):

- ✅ Source code (`.cs`, `.cshtml`, `.js`, `.css`)
- ✅ `appsettings.json` - Template with placeholders (safe)
- ✅ `appsettings.example.json` - Example configuration
- ✅ Project files (`.csproj`, `.sln`)
- ✅ Documentation files (`.md`)
- ✅ Static assets (`wwwroot/`)

## 🚀 Steps to Upload to GitHub

### Step 1: Initialize Git Repository (if not already done)

```bash
cd "d:\MedicalAIPlatform final\MedicalAIPlatform"
git init
```

### Step 2: Add the Remote Repository

```bash
git remote add origin https://github.com/joyibraheem/Multimodal-Medical-AI.git
```

### Step 3: Verify .gitignore is Working

Check what files will be committed:

```bash
git status
```

You should see that sensitive files like `appsettings.Development.json` and `bin/` folders are NOT listed.

### Step 4: Stage Files for Commit

```bash
git add .
```

This will add all files EXCEPT those in `.gitignore`.

### Step 5: Create Initial Commit

```bash
git commit -m "Initial commit: Medical AI Platform"
```

### Step 6: Push to GitHub

```bash
git branch -M main
git push -u origin main
```

## ⚠️ Important Security Checklist

Before pushing, verify:

- [ ] `appsettings.Development.json` is NOT in the commit (check with `git status`)
- [ ] `bin/` and `obj/` folders are NOT in the commit
- [ ] No database files (`.mdf`, `.ldf`) are in the commit
- [ ] No real Google OAuth credentials in `appsettings.json` (should have placeholders)
- [ ] No real connection strings with passwords
- [ ] `.vs/` folder is excluded

## 🔐 If You Accidentally Committed Sensitive Files

If you've already committed sensitive files, you need to remove them from Git history:

```bash
# Remove file from Git but keep locally
git rm --cached appsettings.Development.json

# Commit the removal
git commit -m "Remove sensitive configuration file"

# If already pushed, you'll need to force push (BE CAREFUL)
# git push --force origin main
```

**Warning**: Force pushing rewrites history. Only do this if you're the only one working on the repo.

## 📋 Recommended: Use Environment Variables for Secrets

For production, consider using environment variables or Azure Key Vault instead of storing secrets in files:

```csharp
// In Program.cs, you can use:
var googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") 
    ?? builder.Configuration["Authentication:Google:ClientId"];
```

## 📝 For Contributors

If someone clones your repository, they should:

1. Copy `appsettings.example.json` to `appsettings.Development.json`
2. Fill in their own credentials
3. `appsettings.Development.json` is already in `.gitignore`, so it won't be committed

## ✅ Final Verification

After pushing, check your GitHub repository:

1. Go to: https://github.com/joyibraheem/Multimodal-Medical-AI
2. Verify that:
   - ✅ Source code is visible
   - ✅ `appsettings.json` exists (with placeholders)
   - ✅ `appsettings.example.json` exists
   - ❌ `appsettings.Development.json` is NOT visible
   - ❌ `bin/` folder is NOT visible
   - ❌ `.vs/` folder is NOT visible

## 🆘 Need Help?

If you're unsure about a file:
- Check if it's in `.gitignore`
- If it contains secrets/passwords → Don't commit it
- If it's a build artifact → Don't commit it
- If it's user-specific → Don't commit it

When in doubt, ask before committing!
