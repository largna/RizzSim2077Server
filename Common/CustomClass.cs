using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Azure;
using Azure.Security.KeyVault.Secrets;
using System.Text.Json;

public static class Constants
{
    public const int MAX_TOKEN_PER_MIN = 6000;
    public const int MAX_USER_TOKEN_USAGE = 6000;
}
public class ActivityData
{
    public string UserId { get; set; }
    public DateTime LastActivity { get; set; }
    public int usedTokenPerMin { get; set; }

    public int usedTokenPerDay { get; set; }

    public int totalTokenUsage { get; set; }

    public ActivityData(string userId, DateTime lastActivity, int usedTokenPerMin, int totalTokenUsage)
    {
        UserId = userId;
        LastActivity = lastActivity;
        this.usedTokenPerMin = usedTokenPerMin;
        this.usedTokenPerDay = usedTokenPerDay;
        this.totalTokenUsage = totalTokenUsage;
    }
    
    public bool RequiredUserId { get; }

    // 기본 생성자 (JSON 파싱을 위해 필요할 수 있음)
    public ActivityData() 
    {
        UserId = string.Empty;
        LastActivity = DateTime.MinValue;
        usedTokenPerMin = 0;
        usedTokenPerDay = 0;
        totalTokenUsage = 0;
    }

    public override string ToString()
    {
        return $"UserId: {UserId}, LastActivity: {LastActivity}, usedTokenPerMin: {usedTokenPerMin}, usedTokenPerDay: {usedTokenPerDay} ,totalTokenUsage: {totalTokenUsage}";
    }
}


public class User
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string id { get; set; }
    public string userid  { get; set; }
    public string username { get; set; }

    public string password { get; set; }
    public int usedTokenPerDay { get; set; }
    public int totalTokenUsage { get; set; }

    public DateTime LastActivity { get; set; }

    // etc fields...

    public User()
    {
        // default constructor
        userid = "TestId";
        id = userid;
        username = "TestName";
        password = "TestPassword";
        usedTokenPerDay = 0;
        totalTokenUsage = 0;
        LastActivity = DateTime.Now;
    }

    public override string ToString()
    {
        return $"id: {id}, userid: {userid}, username: {username}, usedTokenPerDay: {usedTokenPerDay}, totalTokenUsage: {totalTokenUsage}";
    }
}
