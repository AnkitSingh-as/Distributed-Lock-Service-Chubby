namespace Chubby.Core.Model;

public class Node
{
    public long Instance { get; private set; }
    public long ContentGenerationNumber { get; set; }
    public long LockGenerationNumber { get; set; }
    public long AclGenerationNumber { get; set; }
    public long ContentLength => content.Length;
    public byte[] content;
    public readonly string name;
    public bool IsEphemeral { get; private set; }
    private NodeType type;

    // CheckSum for contents in future, if required.

    // this will give me the name of the file and I have to read the contents of file to check whether the client has its 
    // name present in the acl name.
    // and contents of acl has to written via some other handle...in ls/Chubby/acl
    // -->for now instead of creating acl directory, and doing the above, just verify the client names again values in the acl arrays...
    public string[] WriteAcl { get; set; }
    public string[] ReadAcl { get; set; }
    public string[] ChangeAcl { get; set; }

    public Node(string path, byte[] content, string[] writeAcl, string[] readAcl, string[] changeAcl, bool isEphemeral, long instance = 1)
    {
        this.name = path;
        this.content = content;
        this.WriteAcl = writeAcl;
        this.ReadAcl = readAcl;
        this.ChangeAcl = changeAcl;
        this.IsEphemeral = isEphemeral;
        this.Instance = instance;
        this.ContentGenerationNumber = 1;
        this.LockGenerationNumber = 0;
        this.AclGenerationNumber = 1;
        this.type = NodeType.File;
    }

    public Node(Node other)
    {
        this.name = other.name;
        this.content = other.content;
        this.WriteAcl = other.WriteAcl;
        this.ReadAcl = other.ReadAcl;
        this.ChangeAcl = other.ChangeAcl;
        this.IsEphemeral = other.IsEphemeral;
        this.Instance = other.Instance;
        this.ContentGenerationNumber = other.ContentGenerationNumber;
        this.LockGenerationNumber = other.LockGenerationNumber;
        this.AclGenerationNumber = other.AclGenerationNumber;
        this.type = other.type;
    }

    public static Node GenerateNextInstance(Node node, byte[] content, string[] writeAcl, string[] readAcl, string[] changeAcl, bool isEphemeral)
    {
        // When a node is re-created after being deleted, it gets a new instance number,
        // and its other generation numbers are reset, just like a brand new node.
        return new Node(node.name, content, writeAcl, readAcl, changeAcl, isEphemeral, node.Instance + 1);
    }
}

