using UnityEngine;

public abstract class BTNode
{
    public abstract bool Execute();
}

public class Selector : BTNode
{
    BTNode[] nodes;
    public Selector(params BTNode[] nodes) => this.nodes = nodes;
    public override bool Execute()
    {
        foreach (var node in nodes)
            if (node.Execute()) return true;
        return false;
    }
}

public class Sequence : BTNode
{
    BTNode[] nodes;
    public Sequence(params BTNode[] nodes) => this.nodes = nodes;
    public override bool Execute()
    {
        foreach (var node in nodes)
            if (!node.Execute()) return false;
        return true;
    }
}

public class Condition : BTNode
{
    public delegate bool ConditionDelegate();
    ConditionDelegate condition;
    public Condition(ConditionDelegate condition) => this.condition = condition;
    public override bool Execute() => condition();
}

public class ActionNode : BTNode
{
    public delegate bool ActionDelegate();
    ActionDelegate action;
    public ActionNode(ActionDelegate action) => this.action = action;
    public override bool Execute() => action();
}