# BitWatch

BitWatch is a desktop-first file integrity and cleanup management tool. It is designed to continuously monitor user-specified directories, compute cryptographic hashes, and detect any unauthorized or silent file modifications, deletions, or corruptions.

## Technologies Used

* **Language**: C#
* **Frameworks & Runtimes**: .NET 9, Avalonia UI (v11.3.9), ReactiveUI.Avalonia
* **Database**: PostgreSQL (intended)
* **Package Manager**: NuGet

## Getting Started

### Prerequisites

* .NET 9 SDK
* A compatible IDE (e.g., Visual Studio, JetBrains Rider) is recommended for development.

### Running the Application

1. ```bash
    cd BitWatch
    ```

2. Run the application using the .NET CLI:

    ```bash
    dotnet run
    ```

### Building the Application

```bash
dotnet build
```

## Project Structure

* `/BitWatch`: Contains the primary source code for the Avalonia application.
  * `/Behaviors`: Holds attached behaviors for UI controls.
  * `/ViewModels`: Contains ViewModel classes for the application's views.
* `BitWatch.csproj`: Project file defining dependencies and build settings.
* `bitwatch.sln`: Visual Studio solution file for the entire project.

## Contributing

Currently, there are no formal contribution guidelines. Please adhere to the existing coding conventions and style within the project.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
