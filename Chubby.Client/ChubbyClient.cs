public class ChubbyClient
{

    // its better I complete the server side, then generate the grpc client and then look forward to the client side, 
    // otherwise the creation of types is a mess. 


    private readonly IChubby _chubby;
    private readonly string _name;
    public ChubbyClient(IChubby chubby, string name)
    {
        _chubby = chubby;
        _name = name;
    }

    public async Task CreateSession()
    {
        try
        {
            // await _chubby.CreateSession(new
            // {
            //     Name = _name
            // });
        }
        catch (Exception ex)
        {

        }
        finally
        {

        }

    }
}