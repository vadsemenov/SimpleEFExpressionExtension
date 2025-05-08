using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

await using SimpleDbContext dbContext = new SimpleDbContext();

bool isDbCreated = await dbContext.Database.EnsureCreatedAsync();

if (isDbCreated)
{
	await dbContext.AddEntities();
}

//Фильтр по нескольким условиям, выбираются сущности для которых одно из условий верно. 
List<Order> ordersWithOrConditions = dbContext.Orders
	.Include(o => o.Customer)
	.WhereOrConditions(o => o.Customer.FirstName == "John",
		o => o.ProductName == "Onion")
	.ToList();

//Фильтр по нескольким условиям, выбираются сущности для которых все условия верны. 
List<Order> ordersWithAndConditions = dbContext.Orders
	.Include(o => o.Customer)
	.WhereAndConditions(o => o.Customer.FirstName == "John",
		o => o.ProductName == "Onion")
	.ToList();

//Фильтр по дате, в указанном диапазоне дат.
List<Order> ordersInDateTimeRange = dbContext.Orders
	.WhereDateTimeBetween(x => x.DateTime, new DateTime(2025, 4, 25), DateTime.Now)
	.ToList();

//Фильтр по наличию подстроки в строковых свойствах
List<Order> ordersWithPropertiesContainsText = dbContext.Orders
	.Include(o => o.Customer)
	.WhereAnyPropertyContainText("e",
		o => o.Customer.FirstName,
		o => o.ProductName)
	.ToList();

Console.Read();

public static class SimpleQueryableExtensions
{
	
	// Метод фильтрует все свойства которые содержат текст 
	public static IQueryable<T> WhereAnyPropertyContainText<T>(
		this IQueryable<T> source,
		string searchText,
		params Expression<Func<T, string>>[] properties)
	{
		//Это параметр, который будет использоваться во всех лямбдах массива properties, например x=>x.Customer.FirstName
		//Имя параметра x
		ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
		//Достаем метод Contains для вызова поиска текста
		MethodInfo? containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) });
		
		if (containsMethod == null)
			throw new InvalidOperationException("Contains method not found.");
		
		Expression body = Expression.Constant(false);
		//Проходим по всему массиву лямбд properties, и для каждого выражения
		//проверяем содержание текста searchText через вызов метода Contains.
		//Если выражение(свойство) содержит текст, то добавляем выражение к результирующему дереву выражений 
		foreach (Expression<Func<T, string>> property in properties)
		{
			// Заменяем параметр исходного выражения на наш параметр
			// если имя параметр в лямбде не x, то меняем лямбду с o => o.ProductName на x => x.ProductName
			// Имя параметра x задано выше в переменной parameter.
			// Если не менять имена параметров в лямбдах, и они буду разные, то будет ошибка
			Expression propertyAccess = ReplaceAnotherParameter(property.Body, property.Parameters[0], parameter);
			MethodCallExpression containsCall = Expression.Call(propertyAccess, containsMethod, Expression.Constant(searchText));
			body = Expression.OrElse(body, containsCall);
		}
		
		//Возвращаем результирующую лямбду
		//На дебаге можно поставить точку останова и посмотреть ее
		Expression<Func<T, bool>> lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
		return source.Where(lambda);
	}
	
	//Метод фильтрует по свойству типа DateTime, и выбирает все сущности во временном диапазоне
	public static IQueryable<T> WhereDateTimeBetween<T>(this IQueryable<T> source,
		Expression<Func<T, DateTime>> dateSelector,
		DateTime startDate, DateTime endDate)
	{
		//Параметер используемый в лямбде
		ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
		
		//Преобразуем делегат в аргумент(результирующую дату)
		InvocationExpression dateAccess = Expression.Invoke(dateSelector, parameter);
		
		//Сравниваем что дата больше startDate
		BinaryExpression lowerBound = Expression.GreaterThanOrEqual(dateAccess, Expression.Constant(startDate));
		//Сравниваем что дата меньше endDate
		BinaryExpression upperBound = Expression.LessThanOrEqual(dateAccess, Expression.Constant(endDate));
		
		//Проводим операцию AND, если оба предыдущих выражения верны возвращаем True
		BinaryExpression body = Expression.AndAlso(lowerBound, upperBound);
		
		//Возвращаем результирующую лямбду
		//На дебаге можно поставить точку останова и посмотреть ее
		Expression<Func<T, bool>> lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
		
		return source.Where(lambda);
	}
	
	//Метод фильтрует сущности по массиву лямбд predicates.
	//Если все лямбды возвращают true, то сущность добавляется в набор
	public static IQueryable<T> WhereAndConditions<T>(this IQueryable<T> source,
		params Expression<Func<T, bool>>[] predicates)
	{
		return source.WhereConditions(ConditionType.And, predicates);
	}
	
	//Метод фильтрует сущности по массиву лямбд predicates.
	//Если одна из лямбд возвращают true, то сущность добавляется в набор
	public static IQueryable<T> WhereOrConditions<T>(this IQueryable<T> source,
		params Expression<Func<T, bool>>[] predicates)
	{
		return source.WhereConditions(ConditionType.Or, predicates);
	}
	
	//Метод фильтрует сущности по массиву лямбд predicates.
	private static IQueryable<T> WhereConditions<T>(this IQueryable<T> source, ConditionType conditionType, params Expression<Func<T, bool>>[] predicates)
	{
		if (predicates.Length == 0)
			return source;
		
		if (predicates.Length == 1)
			return source.Where(predicates[0]);
		
		//Параметр используемый в лямбдах
		ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
		
		// Заменяем параметр исходного выражения на наш параметр
		// если имя параметр в лямбде не x, то меняем лямбду с o => o.ProductName на x => x.ProductName
		// Имя параметра x задано выше в переменной parameter.
		// Если не менять имена параметров в лямбдах, и они буду разные, то будет ошибка
		List<Expression<Func<T, bool>>> replacedPredicates = predicates.Select(p =>
		{
			var newBody = ReplaceAnotherParameter(p.Body, p.Parameters[0], parameter);
			return Expression.Lambda<Func<T, bool>>(newBody, parameter);
		}).ToList();
		
		Expression body = replacedPredicates[0].Body;
		//Проходим по всему массиву лямбд replacedPredicates, и для каждого выражения
		//Строим новое дерево выражений body, в зависимости от условия добавляем сравнение AND или OR
		for (int i = 1; i < replacedPredicates.Count; i++)
		{
			body = conditionType switch
			{
				ConditionType.And => Expression.AndAlso(body, replacedPredicates[i].Body),
				ConditionType.Or => Expression.OrElse(body, replacedPredicates[i].Body),
				_ => throw new ArgumentOutOfRangeException(nameof(conditionType), conditionType, null)
			};
		}
		
		//Возвращаем результирующую лямбду
		//На дебаге можно поставить точку останова и посмотреть ее
		Expression<Func<T, bool>> lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
		
		return source.Where(lambda);
	}
	
	//Перечисление для изменения типа операции
	private enum ConditionType
	{
		And,
		Or
	}
	
	//Вспомогательный класс для замены старого параметра на новый
	private static Expression ReplaceAnotherParameter(
		Expression expression,
		ParameterExpression oldParameter,
		ParameterExpression newParameter)
	{
		ParameterReplacerVisitor visitor = new ParameterReplacerVisitor(oldParameter, newParameter);
		return visitor.Visit(expression);
	}
	
	//Класс наследуется от ExpressionVisitor
	//ExpressionVisitor - это специальный класс, который позволяет пройти по всем узлам
	//дерева выражений (ExpressionTree) и модифицировать их так как нам нужно.
	private class ParameterReplacerVisitor(
		ParameterExpression oldParameter,
		ParameterExpression newParameter) : ExpressionVisitor
	{
		//Переопределяем базовый метод VisitParameter, и заменяем старый параметр в лямбде на новый
		// то есть, если старый параметр x а новый y, то меняем лямбда поменяется с x=>x+1 на y=>y+1
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
		optionsBuilder.UseSqlServer("Server=localhost;Database=ExpressionDb;TrustServerCertificate=True;");
	}
}

public static class EntityCreator
{
	public static async Task AddEntities(this SimpleDbContext context)
	{
		Customer customer1 = new Customer
		{
			FirstName = "John",
			LastName = "Doe",
			Age = 15
		};
		
		Customer customer2 = new Customer
		{
			FirstName = "Petr",
			LastName = "Petrov",
			Age = 30
		};
		
		Customer customer3 = new Customer
		{
			FirstName = "Pettr",
			LastName = "Pettrov",
			Age = 31
		};
		
		await context.Customers.AddRangeAsync(customer1, customer2, customer3);
		await context.SaveChangesAsync();
		
		Order order1 = new Order
		{
			OrderNumber = "1",
			ProductName = "Tomato",
			DateTime = DateTime.Now - TimeSpan.FromDays(3),
			Customer = customer1
		};
		
		Order order2 = new Order
		{
			OrderNumber = "1",
			ProductName = "Onion",
			DateTime = DateTime.Now - TimeSpan.FromDays(2),
			Customer = customer1
		};
		
		Order order3 = new Order
		{
			OrderNumber = "1",
			ProductName = "Banana",
			DateTime = DateTime.Now - TimeSpan.FromDays(1),
			Customer = customer2
		};
		
		Order order4 = new Order
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
