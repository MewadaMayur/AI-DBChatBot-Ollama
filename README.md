# DBChatbot — AI-Powered SQL Server Assistant

## Overview

DBChatbot is an ASP.NET Core web application that uses a locally hosted Ollama language model to answer natural-language questions about SQL Server databases.

The application combines SQL Server metadata and database-object relationship information with AI-generated explanations through a web-based chatbot UI.

## Features

- Natural-language questions about SQL Server databases
- Search for tables, columns, stored procedures, views, functions, and synonyms
- Database-object dependency and relationship discovery
- Stored procedure explanation and operation detection
- Support for multiple approved databases
- Local AI inference through Ollama
- ASP.NET Core MVC/Razor user interface
- Bootstrap 5.3-based responsive UI

## Technology Stack

- ASP.NET Core / C#
- .NET 10
- Microsoft SQL Server
- ADO.NET
- Ollama
- `qwen2.5-coder:7b`
- ASP.NET Core MVC / Razor
- Bootstrap 5.3
- Git

## Prerequisites

Install the following before running the application:

1. Git
2. .NET 10 SDK
3. SQL Server or access to an existing SQL Server instance
4. Ollama
5. The required Ollama model
6. A modern web browser

SSMS is recommended for verifying SQL Server connectivity.

## 1. Clone the Repository

Open a terminal and run:

```bash
git clone <YOUR_GIT_REPOSITORY_URL>
cd <YOUR_REPOSITORY_FOLDER>
```

Replace the placeholders with your actual Git repository URL and folder name.

## 2. Configure SQL Server

Open `appsettings.json` and configure the `SqlServer` connection string to point to your SQL Server instance.

Example only:

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=YOUR_SERVER;Database=master;Integrated Security=True;TrustServerCertificate=True;"
  }
}
```

Use the connection-string format expected by this project. If SQL authentication is required, configure the appropriate credentials securely.

**Important:** Do not commit passwords, production connection strings, API keys, or other secrets to Git.

The SQL Server account must have the required permissions to read metadata from the approved databases and inspect database-object definitions.

## 3. Install and Start Ollama

Install Ollama from:

https://ollama.com/

Open a terminal and download the required model:

```bash
ollama pull qwen2.5-coder:7b
```

Start the Ollama service if it is not already running.

Verify the installed models:

```bash
ollama list
```

Verify that Ollama responds:

```bash
curl http://localhost:11434/api/tags
```

The application must be configured to use the same model name and Ollama endpoint.

## 4. Restore Dependencies

From the directory containing the `.csproj` file, run:

```bash
dotnet restore
```

## 5. Build the Project

```bash
dotnet build
```

Resolve any build errors before proceeding.

## 6. Run the Application

```bash
dotnet run
```

Use the HTTP or HTTPS URL displayed in the terminal. For example, a local development URL may look like:

- `http://localhost:5237`
- `https://localhost:7173`

The actual URL depends on the project's launch settings.

## 7. Use the Chatbot

1. Open the application URL in a browser.
2. Select an approved database from the database dropdown.
3. Enter a question in natural language.
4. Submit the question.
5. Review the AI-generated answer and any supporting evidence.

### Example Questions

- Which stored procedure creates an application?
- Which stored procedures update `ApplicationMain`?
- Which views and stored procedures reference `ApplicationMain`?
- What columns exist in `LeadMain`?
- Explain the purpose of a particular stored procedure.
- Which database objects depend on a specified table?

## Configuration and Security

- Only databases explicitly approved in the application configuration should be accessible.
- The SQL Server login should follow the principle of least privilege.
- Keep credentials out of source control.
- Do not expose the development server or Ollama endpoint publicly without appropriate security controls.
- Treat AI-generated explanations as guidance and verify important findings against SQL Server metadata and source definitions.

## Troubleshooting

### SQL Server connection fails
Check the connection string, SQL Server availability, authentication, network access, and database permissions.

### Ollama is unavailable
Check that Ollama is running and that the configured model is installed.

### Model not found
Run `ollama list` and confirm that the model name in the application configuration matches the installed model.

### Application does not build
Confirm that the required .NET SDK is installed, then run `dotnet restore` and `dotnet build`.

### Answers are slow
The first request may take longer while the model loads. Performance also depends on the model size and available hardware.

## Development Status

This project is under active development. Features and configuration may change as database-object discovery and answer accuracy improve.
