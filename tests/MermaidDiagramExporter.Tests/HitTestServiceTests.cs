using SkiaSharp;
using Xunit;
using MermaidDiagramExporter.Gui;

namespace MermaidDiagramExporter.Tests;

public class HitTestServiceTests
{
    [Fact]
    public void ScreenToWorld_IdentityTransform_ReturnsSameCoordinates()
    {
        // Arrange
        float screenX = 100f;
        float screenY = 200f;
        float panX = 0f;
        float panY = 0f;
        float zoom = 1f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        Assert.Equal(100f, result.X);
        Assert.Equal(200f, result.Y);
    }

    [Fact]
    public void ScreenToWorld_PanOnlyTransform_ReturnsOffsetCoordinates()
    {
        // Arrange
        float screenX = 150f;
        float screenY = 250f;
        float panX = 50f;
        float panY = 100f;
        float zoom = 1f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        Assert.Equal(100f, result.X);
        Assert.Equal(150f, result.Y);
    }

    [Fact]
    public void ScreenToWorld_ZoomOnlyTransform_ReturnsScaledCoordinates()
    {
        // Arrange
        float screenX = 200f;
        float screenY = 300f;
        float panX = 0f;
        float panY = 0f;
        float zoom = 2f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        Assert.Equal(100f, result.X);
        Assert.Equal(150f, result.Y);
    }

    [Fact]
    public void ScreenToWorld_CombinedPanAndZoomTransform_ReturnsCorrectCoordinates()
    {
        // Arrange
        float screenX = 250f;
        float screenY = 350f;
        float panX = 50f;
        float panY = 50f;
        float zoom = 2f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        // (250 - 50) / 2 = 100
        // (350 - 50) / 2 = 150
        Assert.Equal(100f, result.X);
        Assert.Equal(150f, result.Y);
    }

    [Fact]
    public void ScreenToWorld_NegativeCoordinates_ReturnsCorrectCoordinates()
    {
        // Arrange
        float screenX = -50f;
        float screenY = -150f;
        float panX = -10f;
        float panY = 50f;
        float zoom = 0.5f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        // (-50 - -10) / 0.5 = -40 / 0.5 = -80
        // (-150 - 50) / 0.5 = -200 / 0.5 = -400
        Assert.Equal(-80f, result.X);
        Assert.Equal(-400f, result.Y);
    }

    [Fact]
    public void ScreenToWorld_ZeroZoom_ReturnsInfinity()
    {
        // Arrange
        float screenX = 100f;
        float screenY = -200f; // negative to test NegativeInfinity
        float panX = 0f;
        float panY = 0f;
        float zoom = 0f;

        // Act
        var result = HitTestService.ScreenToWorld(screenX, screenY, panX, panY, zoom);

        // Assert
        Assert.Equal(float.PositiveInfinity, result.X);
        Assert.Equal(float.NegativeInfinity, result.Y);
    }
}
