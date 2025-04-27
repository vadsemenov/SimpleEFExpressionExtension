using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

await using var dbContext = new SimpleDbContext();

var isDbCreated = await dbContext.Database.EnsureCreatedAsync();

if (isDbCreated)
{
	await dbContext.AddEntities();
}

//Фильтр по нескольким условиям, выбираются сущности для которых одно из условий верно. 
var ordersWithOrConditions = dbContext.Orders
	.Include(o => o.Customer)
	.WhereOrConditions(o => o.Customer.FirstName == "John",
		o => o.ProductName == "Onion")
	.ToList();

//Фильтр по нескольким условиям, выбираются сущности для которых все условия верны. 
var ordersWithAndConditions = dbContext.Orders
	.Include(o => o.Customer)
	.WhereAndConditions(o => o.Customer.FirstName == "John",
		o => o.ProductName == "Onion")
	.ToList();

//Фильтр по дате, в указанном диапазоне дат.
var ordersInDateTimeRange = dbContext.Orders
	.WhereDateTimeBetween(x => x.DateTime, new DateTime(2025, 4, 25), DateTime.Now)
	.ToList();

//Фильтр по наличию подстроки в строковых свойствах
var ordersWithPropertiesContainsText = dbContext.Orders
	.Include(o=>o.Customer)
	.WhereAnyPropertyContainText("e",
		o => o.Customer.FirstName,
		o => o.ProductName)
	.ToList();

Console.Read();

public static class SimpleQueryableExtensions
{
	public static IQueryable<T> WhereAnyPropertyContainText<T>(
		this IQueryable<T> source,
		string searchTerm,
		params Expression<Func<T, string>>[] properties)
	{
		var parameter = Expression.Parameter(typeof(T), "x");
		var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) });
		
		if (containsMethod == null)
			throw new InvalidOperationException("Contains method not found.");
		
		Expression body = Expression.Constant(false);
		foreach (var property in properties)
		{
			// Заменяем параметр исходного выражения на наш параметр
			var propertyAccess = ReplaceAnotherParameter(property.Body, property.Parameters[0], parameter);
			var containsCall = Expression.Call(propertyAccess, containsMethod, Expression.Constant(searchTerm));
			body = Expression.OrElse(body, containsCall);
		}
		
		var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
		return source.Where(lambda);
	}
	
	public static IQueryable<T> WhereDateTimeBetween<T>(this IQueryable<T> source,
		Expression<Func<T, DateTime>> dateSelector, 
		DateTime startDate, DateTime endDate)
	{
		var parameter = Expression.Parameter(typeof(T), "x");
		var dateAccess = Expression.Invoke(dateSelector, parameter);
		
		var lowerBound = Expression.GreaterThanOrEqual(dateAccess, Expression.Constant(startDate));
		var upperBound = Expression.LessThanOrEqual(dateAccess, Expression.Constant(endDate));
		
		var body = Expression.AndAlso(lowerBound, upperBound);
		
		var lambda =  Expression.Lambda<Func<T, bool>>(body, parameter);
		
		return source.Where(lambda);
	}
	
	public static IQueryable<T> WhereAndConditions<T>(this IQueryable<T> source,
		params Expression<Func<T, bool>>[] predicates)
	{
		return source.WhereConditions(ConditionType.And, predicates);
	}
	
	public static IQueryable<T> WhereOrConditions<T>(this IQueryable<T> source,
		params Expression<Func<T, bool>>[] predicates)
	{
		return source.WhereConditions(ConditionType.Or, predicates);
	}
	
	private static IQueryable<T> WhereConditions<T>(this IQueryable<T> source, ConditionType conditionType, params Expression<Func<T, bool>>[] predicates)
	{
		if (predicates.Length == 0)
			return source;
		
		if (predicates.Length == 1)
			return source.Where(predicates[0]);
		
		var parameter = Expression.Parameter(typeof(T), "x");
		var replacedPredicates = predicates.Select(p => ReplaceParameter(p, parameter)).ToList();
		
		var body = replacedPredicates[0].Body;
		for (int i = 1; i < replacedPredicates.Count; i++)
		{
			body = conditionType switch
			{
				ConditionType.And => Expression.AndAlso(body, replacedPredicates[i].Body),
				ConditionType.Or => Expression.OrElse(body, replacedPredicates[i].Body),
				_ => throw new ArgumentOutOfRangeException(nameof(conditionType), conditionType, null)
			};
		}
		
		var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
		
		return source.Where(lambda);
	}
	
	private enum ConditionType
	{
		And,
		Or
	}
	
	private static Expression ReplaceAnotherParameter(
		Expression expression,
		ParameterExpression oldParameter,
		ParameterExpression newParameter)
	{
		var visitor = new ParameterReplacerVisitor(oldParameter, newParameter);
		return visitor.Visit(expression);
	}
	
	private static Expression<Func<T, bool>> ReplaceParameter<T>(
		Expression<Func<T, bool>> predicate,
		ParameterExpression newParameter)
	{
		var visitor = new ParameterReplacerVisitor(predicate.Parameters[0], newParameter);
		var newBody = visitor.Visit(predicate.Body);
		return Expression.Lambda<Func<T, bool>>(newBody, newParameter);
	}
	
	private class ParameterReplacerVisitor(ParameterExpression oldParameter,
		ParameterExpression newParameter) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node)
		{
			return node == oldParameter ? newParameter : base.VisitParameter(node);
		}
	}
}

public class SimpleDbContext : DbContext
{
	public DbSet<Order> Orders { get; set; }
	public DbSet<Customer> Customers { get; set; }
	
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlServer("Server=localhost;Database=ExpressionDb;User Id=sa;Password=131564471;TrustServerCertificate=True;");
	}
}

public static class EntityCreator
{
	public static async Task AddEntities(this SimpleDbContext context)
	{
		var customer1 = new Customer
		{
			FirstName = "John",
			LastName = "Doe",
			Age = 15
		};
		
		var customer2 = new Customer
		{
			FirstName = "Petr",
			LastName = "Petrov",
			Age = 30
		};
		
		var customer3 = new Customer
		{
			FirstName = "Pettr",
			LastName = "Pettrov",
			Age = 31
		};
		
		await context.Customers.AddRangeAsync(customer1, customer2, customer3);
		await context.SaveChangesAsync();
		
		var order1 = new Order
		{
			OrderNumber = "1",
			ProductName = "Tomato",
			DateTime = DateTime.Now - TimeSpan.FromDays(3),
			Customer = customer1
		};
		
		var order2 = new Order
		{
			OrderNumber = "1",
			ProductName = "Onion",
			DateTime = DateTime.Now - TimeSpan.FromDays(2),
			Customer = customer1
		};
		
		var order3 = new Order
		{
			OrderNumber = "1",
			ProductName = "Banana",
			DateTime = DateTime.Now - TimeSpan.FromDays(1),
			Customer = customer2
		};
		
		var order4 = new Order
		{
			OrderNumber = "1",
			ProductName = "Chery",
			DateTime = DateTime.Now,
			Customer = customer3
		};
		
		await context.Orders.AddRangeAsync(order1, order2, order3, order4);
		await context.SaveChangesAsync();
	}
}

public class Order
{
	public int Id { get; set; }
	[MaxLength(250)]
	public string OrderNumber { get; set; } = null!;
	[MaxLength(250)]
	public string ProductName { get; set; } = null!;
	public DateTime DateTime { get; set; }
	public int CustomerId { get; set; }
	public Customer Customer { get; set; } = null!;
}

public class Customer
{
	public int Id { get; set; }
	[MaxLength(250)]
	public string FirstName { get; set; } = null!;
	[MaxLength(250)]
	public string LastName { get; set; } = null!;
	public int Age { get; set; }
	public ICollection<Order> Orders { get; set; } = null!;
}