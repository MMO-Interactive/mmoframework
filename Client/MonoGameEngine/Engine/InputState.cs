using Microsoft.Xna.Framework.Input;

namespace MonoGameEngine.Engine;

public sealed class InputState
{
    private KeyboardState _currentKeyboard;
    private KeyboardState _previousKeyboard;

    public KeyboardState CurrentKeyboard => _currentKeyboard;

    public void Update()
    {
        _previousKeyboard = _currentKeyboard;
        _currentKeyboard = Keyboard.GetState();
    }

    public bool IsDown(Keys key) => _currentKeyboard.IsKeyDown(key);

    public bool IsPressed(Keys key) => _currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    public IReadOnlyList<Keys> PressedKeysThisFrame()
    {
        var keys = _currentKeyboard.GetPressedKeys();
        var pressed = new List<Keys>(keys.Length);

        foreach (var key in keys)
        {
            if (_previousKeyboard.IsKeyUp(key))
            {
                pressed.Add(key);
            }
        }

        return pressed;
    }
}
