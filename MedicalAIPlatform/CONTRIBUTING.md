# Contributing to Multimodal Medical AI Platform

Thank you for your interest in contributing! This guide will help you get started.

## 🔗 Repository

**GitHub Repository**: https://github.com/joyibraheem/Multimodal-Medical-AI

## 🚀 Getting Started

### 1. Fork and Clone

```bash
# Fork the repository on GitHub, then clone your fork
git clone https://github.com/YOUR_USERNAME/Multimodal-Medical-AI.git
cd Multimodal-Medical-AI/MedicalAIPlatform
```

### 2. Setup Development Environment

```bash
# Copy example configuration
copy MedicalAIPlatform\appsettings.example.json MedicalAIPlatform\appsettings.Development.json

# Edit appsettings.Development.json with your settings
# - Database connection string
# - Google OAuth credentials (see GOOGLE_AUTH_SETUP.md)
# - API endpoints
```

### 3. Install Dependencies

```bash
# Restore NuGet packages
dotnet restore

# Setup database
dotnet ef database update
```

### 4. Run the Application

```bash
dotnet run
```

The application will be available at `https://localhost:7178`

## 📝 Development Workflow

### Creating a Feature Branch

```bash
# Create and switch to a new branch
git checkout -b feature/your-feature-name

# Or for bug fixes
git checkout -b fix/issue-description
```

### Making Changes

1. Make your code changes
2. Test your changes locally
3. Ensure code follows project conventions
4. Update documentation if needed

### Committing Changes

```bash
# Stage your changes
git add .

# Commit with a descriptive message
git commit -m "Add: Description of your changes"
```

**Commit Message Guidelines**:
- Use present tense: "Add feature" not "Added feature"
- Be descriptive but concise
- Prefix with type: `Add:`, `Fix:`, `Update:`, `Remove:`

### Pushing and Creating Pull Request

```bash
# Push to your fork
git push origin feature/your-feature-name
```

Then create a Pull Request on GitHub:
1. Go to https://github.com/joyibraheem/Multimodal-Medical-AI
2. Click "New Pull Request"
3. Select your branch
4. Fill out the PR template
5. Submit for review

## 🔒 Security Guidelines

### Never Commit:

- ❌ `appsettings.Development.json` (your local config)
- ❌ `appsettings.Production.json` (production secrets)
- ❌ Database files (`.mdf`, `.ldf`, `.db`)
- ❌ Build artifacts (`bin/`, `obj/`)
- ❌ API keys or secrets
- ❌ Passwords or connection strings

### Always Use:

- ✅ `appsettings.example.json` as a template
- ✅ Environment variables for secrets
- ✅ `.gitignore` to exclude sensitive files

## 📋 Code Style

- Follow C# coding conventions
- Use meaningful variable names
- Add comments for complex logic
- Keep functions focused and small
- Write self-documenting code

## 🧪 Testing

Before submitting a PR:

- [ ] Test your changes locally
- [ ] Verify no console errors
- [ ] Check that existing features still work
- [ ] Test in both light and dark mode
- [ ] Test in both English and Arabic (if applicable)

## 📚 Documentation

When adding new features:

- Update `README.md` if needed
- Add comments to complex code
- Update relevant `.md` files
- Document API changes

## 🐛 Reporting Issues

If you find a bug:

1. Check if it's already reported
2. Create a new issue with:
   - Clear description
   - Steps to reproduce
   - Expected vs actual behavior
   - Screenshots if applicable

## 💡 Feature Requests

For new features:

1. Check existing issues/PRs
2. Create an issue describing:
   - The feature
   - Why it's needed
   - How it should work

## ✅ Pull Request Checklist

Before submitting a PR:

- [ ] Code follows project style
- [ ] No sensitive files included
- [ ] Changes tested locally
- [ ] Documentation updated
- [ ] Commit messages are clear
- [ ] No merge conflicts
- [ ] `.gitignore` excludes sensitive files

## 🤝 Code Review Process

1. Submit your PR
2. Wait for review
3. Address feedback
4. Make requested changes
5. PR will be merged when approved

## 📞 Getting Help

- Check existing documentation
- Review `README.md` and setup guides
- Ask questions in PR comments
- Create an issue for bugs

## 🙏 Thank You!

Your contributions make this project better. Thank you for taking the time to contribute!
