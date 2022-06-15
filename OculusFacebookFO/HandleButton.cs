namespace OculusFacebookFO;

public enum HandleButton
{
    Ignore,
    Click,
    ScrollClick,
}

public class ButtonAction
{
    public string Name { get; set; }
    public HandleButton Handle { get; set; }

    public ButtonAction()
    {
        
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name}: {Handle}";
    }
}