using System.Data;
using Dapper;

namespace Iptv.Data.Sqlite;

internal sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(IDbDataParameter parameter, Guid value) => parameter.Value = value.ToString();

    public override Guid Parse(object value) => Guid.Parse((string)value);
}

internal sealed class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
{
    public override void SetValue(IDbDataParameter parameter, Guid? value) =>
        parameter.Value = value is null ? DBNull.Value : value.Value.ToString();

    public override Guid? Parse(object value) => value is null or DBNull ? null : Guid.Parse((string)value);
}
