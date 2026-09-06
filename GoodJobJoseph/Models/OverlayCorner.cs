namespace JosephExperience.Models;

/// <summary>Screen corner where a spinning 3D model (or overlay) is anchored.</summary>
public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

/// <summary>Supported 3D model formats.</summary>
public enum Model3DFormat
{
    None,
    Obj,
    Stl
}