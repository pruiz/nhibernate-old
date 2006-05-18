using System;
using NHibernate.Type;

namespace NHibernate.Loader.Custom
{
	public class SQLQueryScalarReturn
	{
		private IType type;
		private string columnAlias;

		public SQLQueryScalarReturn( string alias, IType type )
		{
			this.type = type;
			this.columnAlias = columnAlias;
		}

		public string ColumnAlias
		{
			get { return columnAlias; }
		}

		public IType Type
		{
			get { return type; }
		}
	}
}