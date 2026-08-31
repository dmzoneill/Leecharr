// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Authentication;

public interface IUserService
{
    User Authenticate(string username, string password);

    User CreateUser(string username, string password, string email = null, string displayName = null, List<string> roles = null);

    User GetById(int id);

    User GetByIdentifier(Guid identifier);

    User GetByUsername(string username);

    List<User> GetAll();

    User Update(User user);

    void UpdatePassword(int userId, string newPassword);

    void Delete(int id);

    string HashPassword(string password, out string salt);

    bool VerifyPassword(string password, string hash, string salt, int iterations);

    bool HasAnyUsers();
}
