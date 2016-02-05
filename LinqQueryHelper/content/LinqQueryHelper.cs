using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LinqQueryHelper
{
    /// <summary>
    /// A class containing generic expression builder methods
    /// </summary>
    public sealed class ExpressionBuilder
    {
        private static readonly MethodInfo ContainsMethod = typeof(string).GetMethod("Contains");
        private static readonly MethodInfo StartsWithMethod =
        typeof(string).GetMethod("StartsWith", new[] { typeof(string) });
        private static readonly MethodInfo EndsWithMethod =
        typeof(string).GetMethod("EndsWith", new[] { typeof(string) });

        /// <summary>
        /// Exprsesion builder for a list of filters
        /// </summary>
        /// <typeparam name="T">A generic entity</typeparam>
        /// <param name="filters">Information about a collection of where clauses</param>
        /// <returns></returns>
        public static Expression<Func<T,
        bool>> GetExpression<T>(IList<Filter> filters)
        {
            if (filters.Count == 0)
                return null;

            ParameterExpression param = Expression.Parameter(typeof(T), "t");
            Expression exp = null;

            if (filters.Count == 1)
                exp = GetExpression<T>(param, filters[0]);
            else if (filters.Count == 2)
                exp = GetExpression<T>(param, filters[0], filters[1]);
            else
            {
                while (filters.Count > 0)
                {
                    var f1 = filters[0];
                    var f2 = filters[1];

                    if (exp == null)
                        exp = GetExpression<T>(param, filters[0], filters[1]);
                    else
                        exp = Expression.AndAlso(exp, GetExpression<T>(param, filters[0], filters[1]));

                    filters.Remove(f1);
                    filters.Remove(f2);

                    if (filters.Count == 1)
                    {
                        exp = Expression.AndAlso(exp, GetExpression<T>(param, filters[0]));
                        filters.RemoveAt(0);
                    }
                }
            }

            return exp == null ? null : Expression.Lambda<Func<T, bool>>(exp, param);
        }

        /// <summary>
        /// Exprsesion builder for a single filter
        /// </summary>
        /// <typeparam name="T">A generic entity</typeparam>
        /// <param name="param">A parameter expression of where clause</param>
        /// <param name="filter">Information about a where caluse</param>
        /// <returns></returns>
        private static Expression GetExpression<T>(ParameterExpression param, Filter filter)
        {
            var paramExpr = NestedExpressionProperty(param, filter.PropertyName);

            Expression constant;
            Expression member;

            if (paramExpr.Type == typeof(DateTime))
            {
                if (filter.Operation == Op.Equals)
                {
                    var timestartstring = Convert.ToDateTime(filter.Value);
                    var timeendtstring = timestartstring.AddMinutes(1440);

                    var const1 = Expression.Constant(Convert.ChangeType(timestartstring, paramExpr.Type));
                    var exp1 = Expression.GreaterThanOrEqual(paramExpr, const1);
                    var const2 = Expression.Constant(Convert.ChangeType(timeendtstring, paramExpr.Type));
                    var exp2 = Expression.LessThan(paramExpr, const2);
                    var exp = Expression.AndAlso(exp1, exp2);
                    return exp;
                }
                constant = Expression.Constant(Convert.ChangeType(filter.Value, paramExpr.Type));
                member = paramExpr;
            }
            else
            {
                var emptyString = Expression.Constant("");
                var coalesceExpr = Expression.Coalesce(paramExpr, emptyString);
                member = Expression.Call(coalesceExpr, "ToString", new Type[0]);
                constant = Expression.Constant(filter.Value);
            }

            switch (filter.Operation)
            {
                case Op.Equals:
                    return Expression.Equal(member, constant);

                case Op.GreaterThan:
                    return Expression.GreaterThan(member, constant);

                case Op.GreaterThanOrEqual:
                    return Expression.GreaterThanOrEqual(member, constant);

                case Op.LessThan:
                    return Expression.LessThan(member, constant);

                case Op.LessThanOrEqual:
                    return Expression.LessThanOrEqual(member, constant);

                case Op.Contains:
                    return Expression.Call(member, ContainsMethod, constant);

                case Op.StartsWith:
                    return Expression.Call(member, StartsWithMethod, constant);

                case Op.EndsWith:
                    return Expression.Call(member, EndsWithMethod, constant);
            }

            return null;
        }

        /// <summary>
        /// Property expression builder
        /// </summary>
        /// <param name="expression">an expression of where clause</param>
        /// <param name="propertyName">database column name or a property name used in a generic expression</param>
        /// <returns></returns>
        public static MemberExpression NestedExpressionProperty(Expression expression, string propertyName)
        {
            var parts = propertyName.Split('.');
            var partsL = parts.Length;

            var exp = (partsL > 1)
                ?
                Expression.Property(
                    NestedExpressionProperty(
                        expression,
                        parts.Take(partsL - 1)
                            .Aggregate((a, i) => a + "." + i)
                    ),
                    parts[partsL - 1])
                :
                Expression.Property(expression, propertyName);

            return exp;
        }

        /// <summary>
        /// Binary expression builder to join multiple where clauses
        /// </summary>
        /// <typeparam name="T">A generic entity</typeparam>
        /// <param name="param">A parameter expression of where clause</param>
        /// <param name="filter1">Information about the first where clause</param>
        /// <param name="filter2">Information about the second where clause</param>
        /// <returns></returns>
        private static BinaryExpression GetExpression<T>
        (ParameterExpression param, Filter filter1, Filter filter2)
        {
            var bin1 = GetExpression<T>(param, filter1);
            var bin2 = GetExpression<T>(param, filter2);

            return Expression.AndAlso(bin1, bin2);
        }

        /// <summary>
        /// A class to carry where clause information
        /// </summary>
        public class Filter
        {
            public string PropertyName { get; set; }
            public Op Operation { get; set; }
            public object Value { get; set; }
        }

        /// <summary>
        /// Enum of where clause operators
        /// </summary>
        public enum Op
        {
            Equals,
            GreaterThan,
            LessThan,
            GreaterThanOrEqual,
            LessThanOrEqual,
            Contains,
            StartsWith,
            EndsWith
        }
    }

    /// <summary>
    /// Sort direction enums
    /// </summary>
    public enum SortDirection
    {
        Ascending,
        Descending
    }

    /// <summary>
    /// A class containing linq query extension methods
    /// </summary>
    public sealed class LinqExtensionMethods
    {
        /// <summary>
        /// A recursive method which combines "where" expressions with "OR" operator
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="filters">Collection of "where" expressions</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> CombineOr<T>(params Expression<Func<T, bool>>[] filters)
        {
            return CombineOr(filters.ToList());
        }

        /// <summary>
        /// A method which combines "where" expressions with "OR" operator
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="filters">A List of "where" expressions</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> CombineOr<T>(List<Expression<Func<T, bool>>> filters)
        {
            if (!filters.Any())
            {
                Expression<Func<T, bool>> alwaysTrue = x => true;
                return alwaysTrue;
            }

            Expression<Func<T, bool>> firstFilter = filters.First();

            var body = firstFilter.Body;
            var param = firstFilter.Parameters.ToArray();
            body = filters.Skip(1).Select(nextFilter => Expression.Invoke(nextFilter, param)).Aggregate(body, (current, nextBody) => Expression.OrElse(current, nextBody));
            Expression<Func<T, bool>> result = Expression.Lambda<Func<T, bool>>(body, param);
            return result;
        }

        /// <summary>
        /// A recursive method which combines "where" expressions with "AND" operator
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="filters">Collection of "where" expressions</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> CombineAnd<T>(params Expression<Func<T, bool>>[] filters)
        {
            return CombineAnd(filters.ToList());
        }

        /// <summary>
        /// A method which combines "where" expressions with "AND" operator
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="filters">A List of "where" expressions</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> CombineAnd<T>(List<Expression<Func<T, bool>>> filters)
        {
            if (!filters.Any())
            {
                Expression<Func<T, bool>> alwaysTrue = x => true;
                return alwaysTrue;
            }
            Expression<Func<T, bool>> firstFilter = filters.First();

            var body = firstFilter.Body;
            var param = firstFilter.Parameters.ToArray();
            body = filters.Skip(1).Select(nextFilter => Expression.Invoke(nextFilter, param)).Aggregate(body, (current, nextBody) => Expression.AndAlso(current, nextBody));
            Expression<Func<T, bool>> result = Expression.Lambda<Func<T, bool>>(body, param);
            return result;
        }

        /// <summary>
        /// A method which performs ORDER BY operation with paging enabled on a generic entity
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="dbSet">A database object on which an order by operation needs to performed</param>
        /// <param name="sortByColumn">A Column name on which a sort opretation needs to be performed. If sort operation needs to performed on any foriegn ket table then a values should passed like "TableName.ColumnName"</param>
        /// <param name="sortDirection">Sort direction which needs to be performed</param>
        /// <param name="skipRecords">Records needs to be skipped for paging</param>
        /// <param name="pageSize">pagesize of a grid or number of records needs to be fetched</param>
        /// <param name="enablePaging">A flag to enable paging</param>
        /// <returns></returns>
        public static IEnumerable<T> OrderBy<T>(IQueryable<T> dbSet, string sortByColumn, SortDirection sortDirection, int skipRecords = 0, int pageSize = 10, bool enablePaging = false)
        {
            var param = Expression.Parameter(typeof(T), "item");

            var property = sortByColumn.Split('.')
                .Aggregate<string, Expression>
                (param, Expression.Property);

            var expression = Expression.Lambda<Func<T, object>>(
                Expression.Convert(property, typeof(object)),
                param);

            var sortExpression = expression.Compile();
            if (enablePaging)
            {
                switch (sortDirection)
                {
                    case SortDirection.Ascending:
                        return dbSet.OrderBy(sortExpression).Skip(skipRecords)
                            .Take(pageSize).ToList();
                    default:
                        return dbSet.OrderByDescending(sortExpression).Skip(skipRecords)
                            .Take(pageSize).ToList();

                }
            }
            switch (sortDirection)
            {
                case SortDirection.Ascending:
                    return dbSet.OrderBy(sortExpression).ToList();
                default:
                    return dbSet.OrderByDescending(sortExpression).ToList();

            }
        }

        /// <summary>
        /// A method which performs ORDER BY operation with "where" condition and paging enabled on a generic entity
        /// </summary>
        /// <typeparam name="T">An entity, a database object or a real time entity</typeparam>
        /// <param name="dbSet">A database object on which an order by operation needs to performed</param>
        /// <param name="sortByColumn">A Column name on which a sort opretation needs to be performed. If sort operation needs to performed on any foriegn ket table then a values should passed like "TableName.ColumnName"</param>
        /// <param name="sortDirection">Sort direction which needs to be performed</param>
        /// <param name="criteria">An "where" expression needs to be excecuted</param>
        /// <param name="skipRecords">Records needs to be skipped for paging</param>
        /// <param name="pageSize">pagesize of a grid or number of records needs to be fetched</param>
        /// <param name="enablePaging">A flag to enable paging</param>
        /// <returns></returns>
        public static IEnumerable<T> OrderBy<T>(IQueryable<T> dbSet, string sortByColumn, SortDirection sortDirection, Expression<Func<T, bool>> criteria, int skipRecords = 0, int pageSize = 10, bool enablePaging = false)
        {
            if (criteria == null) throw new ArgumentNullException("criteria");
            var param = Expression.Parameter(typeof(T), "item");

            var property = sortByColumn.Split('.')
                .Aggregate<string, Expression>
                (param, Expression.Property);

            var expression = Expression.Lambda<Func<T, object>>(
                Expression.Convert(property, typeof(object)),
                param);

            var sortExpression = expression.Compile();
            if (enablePaging)
            {
                switch (sortDirection)
                {
                    case SortDirection.Ascending:
                        return dbSet.Where(criteria).OrderBy(sortExpression).Skip(skipRecords)
                            .Take(pageSize).ToList();
                    default:
                        return dbSet.Where(criteria).OrderByDescending(sortExpression).Skip(skipRecords)
                            .Take(pageSize).ToList();

                }
            }
            switch (sortDirection)
            {
                case SortDirection.Ascending:
                    return dbSet.Where(criteria).OrderBy(sortExpression).ToList();
                default:
                    return dbSet.Where(criteria).OrderByDescending(sortExpression).ToList();

            }
        }
    }
}
