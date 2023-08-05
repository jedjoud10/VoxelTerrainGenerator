using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GraphProcessor;
using System.Linq;

// World position node
[System.Serializable, NodeMenuItem("Inputs/World Position Node")]
public class WorldPositionNode : BaseNode
{
    [Output(name = "World Position")]
    public Vector3 input;

    public override string name => "World Position Node";

    protected override void Process()
    {
    }
}

// Chunk position node
[System.Serializable, NodeMenuItem("Inputs/Chunk Position Node")]
public class ChunkPositionNode: BaseNode
{
    [Output(name = "Chunk Position")]
    public Vector3Int input;

    public override string name => "Chunk Position Node";

    protected override void Process()
    {
    }
}

// Local position node
[System.Serializable, NodeMenuItem("Inputs/Local Position Node")]
public class LocalPositionNode : BaseNode
{
    [Output(name = "Local Position")]
    public Vector3 input;

    public override string name => "Local Position Node";

    protected override void Process()
    {
    }
}