# Multimodal Medical AI Platform

A comprehensive medical AI platform for chest X-ray analysis, clinical text processing, and CT scan analysis using multiple AI models.

## 🚀 Features

- **Multi-Modal AI Analysis**: X-Ray, Clinical Text, and CT Scan analysis
- **Dark Mode Support**: Beautiful dark/light theme switching
- **Multi-Language**: English and Arabic support
- **Google Authentication**: Secure OAuth sign-in
- **Doctor Dashboard**: Comprehensive analytics and patient management
- **AI Assistant**: Integrated medical AI chatbot

## 📋 Prerequisites

- .NET 9.0 SDK
- SQL Server (LocalDB or full instance)
- Python 3.8+ (for CheXNet API)
- Ollama (for AI Assistant - optional)

## 🛠️ Setup Instructions

### 1. Clone the Repository

```bash
git clone https://github.com/joyibraheem/Multimodal-Medical-AI.git
cd Multimodal-Medical-AI/MedicalAIPlatform
```

### 2. Configure Application Settings

Copy the example configuration file:

```bash
# Windows
copy MedicalAIPlatform\appsettings.example.json MedicalAIPlatform\appsettings.Development.json

# Linux/macOS
cp MedicalAIPlatform/appsettings.example.json MedicalAIPlatform/appsettings.Development.json
```

Edit `appsettings.Development.json` and add your:
- Database connection string
- Google OAuth credentials (see [Google Auth Setup Guide](GOOGLE_AUTH_SETUP.md))
- API endpoints

### 2b. Docker (optional)

```bash
cp .env.example .env
docker compose up --build
```

Open http://localhost:8080. See [QUICK_START.md](QUICK_START.md).

### 3. Setup Database

```bash
cd MedicalAIPlatform
dotnet ef database update
```

### 4. Run the Application

```bash
dotnet run
```

The application will be available at `https://localhost:7178`

## 🔐 Security & Privacy

**Important**: Never commit sensitive files to Git. The `.gitignore` file is configured to exclude:
- `appsettings.Development.json`
- `appsettings.Production.json`
- Database files
- Build artifacts
- User-specific files

See [GITHUB_SETUP.md](GITHUB_SETUP.md) for detailed security guidelines.

## 📚 Documentation

- [Quick Start (Docker)](QUICK_START.md)
- [Docker Guide](DOCKER_README.md)
- [Google Authentication Setup](GOOGLE_AUTH_SETUP.md)
- [GitHub Setup & Security](GITHUB_SETUP.md)
- [Testing Google Auth](TEST_GOOGLE_AUTH.md)

## 🏗️ Project Structure

```
MedicalAIPlatform/
├── Controllers/          # MVC Controllers
├── Models/              # Data Models
├── Views/               # Razor Views
├── Services/            # Business Logic Services
├── wwwroot/            # Static Files (CSS, JS, Images)
└── Data/               # Database Context
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-change`)
3. Copy `appsettings.example.json` → `appsettings.Development.json` (never commit the latter)
4. For Docker: copy `.env.example` → `.env` (never commit `.env`)
5. Make your changes and test locally (`dotnet run` or `docker compose up --build`)
6. Ensure no secrets are staged: `git status` must not list `.env` or `appsettings.Development.json`
7. Submit a pull request against `main`

## 📝 License

[Add your license here]

## 👤 Author

**Joy Ibraheem**
- GitHub: [@joyibraheem](https://github.com/joyibraheem)

## 🙏 Acknowledgments

- CheXNet for X-Ray analysis
- BioBERT for clinical text processing
- LungAI for CT scan analysis
