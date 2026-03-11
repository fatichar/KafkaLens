#!/bin/bash

# Test NuGet package creation for KafkaLens.Shared and KafkaLens.Formatting
# This script builds and packages the libraries locally for testing

set -e

echo "🔧 Building and packaging KafkaLens NuGet packages..."

# Get version from Directory.Build.props
VERSION=$(grep -oP '<Version>\K[^<]+' Directory.Build.props)
echo "📦 Version: $VERSION"

# Clean previous builds
echo "🧹 Cleaning previous builds..."
dotnet clean -c Release

# Restore dependencies
echo "📥 Restoring dependencies..."
dotnet restore

# Build projects
echo "🏗️ Building projects..."
dotnet build -c Release -p:Version=$VERSION

# Run tests
echo "🧪 Running tests..."
dotnet test -c Release --no-build

# Create packages
echo "📦 Creating NuGet packages..."
dotnet pack Shared/Shared.csproj -c Release -p:Version=$VERSION --no-build
dotnet pack Formatting/Formatting.csproj -c Release -p:Version=$VERSION --no-build

# List created packages
echo "📋 Created packages:"
find . -name "*.nupkg" -o -name "*.snupkg" | head -10

echo ""
echo "✅ Package creation complete!"
echo ""
echo "To test locally:"
echo "1. Create a local NuGet source: dotnet nuget add source ~/.nuget/local --name \"Local\""
echo "2. Install packages: dotnet add package KafkaLens.Shared --source ~/.nuget/local"
echo "3. Install packages: dotnet add package KafkaLens.Formatting --source ~/.nuget/local"
echo ""
echo "To publish to NuGet.org:"
echo "1. Set NUGET_API_KEY environment variable"
echo "2. Run: dotnet nuget push Shared/bin/Release/KafkaLens.Shared.$VERSION.nupkg -k \$NUGET_API_KEY"
echo "3. Run: dotnet nuget push Formatting/bin/Release/KafkaLens.Formatting.$VERSION.nupkg -k \$NUGET_API_KEY"
