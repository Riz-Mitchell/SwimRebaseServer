namespace Auth.Application.Interfaces;

/// <summary>Remembers recently deleted users so their still-valid access tokens cannot recreate their data.</summary>
public interface IDeletedUserList
{
    void Add(Guid userId);

    bool Contains(Guid userId);
}
